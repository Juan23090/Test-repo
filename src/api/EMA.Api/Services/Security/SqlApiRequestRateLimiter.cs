using System.Data;
using System.Security.Cryptography;
using System.Text;
using EMA.Api.Services.Sandbox;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace EMA.Api.Services.Security;

public sealed class SqlApiRequestRateLimiter :
    IApiRequestRateLimiter
{
    private static readonly HashSet<string>
        VerificationStartFunctions =
        new(StringComparer.Ordinal)
        {
            "StartDonorLoginVerification",
            "StartDonorSelfRegistrationVerification",
            "StartOrganizationLoginVerification",
            "UnifiedLoginLookup",
            "StartPublicOrganizationEmailVerification"
        };

    private static readonly HashSet<string>
        VerificationFunctions =
        new(StringComparer.Ordinal)
        {
            "CompleteUnifiedLogin",
            "VerifyDonorLoginCode",
            "VerifyDonorSelfRegistration",
            "VerifyOrganizationLoginCode",
            "VerifyUnifiedLoginCode",
            "VerifyPublicOrganizationEmail"
        };

    private static readonly HashSet<string>
        LookupFunctions =
        new(StringComparer.Ordinal)
        {
            "DonorOrganizationLoginLookup",
            "SearchPublicOrganizations"
        };

    private static readonly HashSet<string>
        SandboxFunctions =
        new(StringComparer.Ordinal)
        {
            "CreateSandbox",
            "GetCurrentSandbox",
            "ResumeSandbox"
        };

    private static readonly HashSet<string>
        ExpensiveFunctions =
        new(StringComparer.Ordinal)
        {
            "GetChequeReport",
            "GetEmaStatistics",
            "GetGiftReport",
            "GetJournalEntryDetails",
            "GetJournalEntryDetailsCsv",
            "GetJournalEntryDetailsPdf",
            "GetSummaryOfGifts",
            "GetTributeReport",
            "GetYearEndReceiptPreview",
            "PostSavingsCalculatorPdf",
            "PostYearEndReceiptPdf",
            "RunReport"
        };

    private static readonly HashSet<string>
        UploadFunctions =
        new(StringComparer.Ordinal)
        {
            "BlobPut"
        };

    private static readonly HashSet<string>
        OutboundEmailFunctions =
        new(StringComparer.Ordinal)
        {
            "PostSavingsCalculatorEmail",
            "StartDonorLoginVerification",
            "StartDonorSelfRegistrationVerification",
            "StartEmailVerification",
            "StartOrganizationLoginVerification",
            "UnifiedLoginLookup",
            "StartPublicOrganizationEmailVerification"
        };

    private static readonly HashSet<string>
        PublicWriteFunctions =
        new(StringComparer.Ordinal)
        {
            "SubmitPublicOrganizationRequest"
        };

    private readonly string _connectionString;
    private readonly byte[] _keySecret;

    public SqlApiRequestRateLimiter(
        IConfiguration configuration)
    {
        _connectionString =
            configuration["SqlConnectionString"]
            ?? Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString.");

        var configuredSecret =
            configuration["API_RATE_LIMIT_SECRET"];

        if (string.IsNullOrWhiteSpace(
                configuredSecret))
        {
            throw new InvalidOperationException(
                "Missing API_RATE_LIMIT_SECRET.");
        }

        try
        {
            _keySecret =
                Convert.FromBase64String(
                    configuredSecret);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "API_RATE_LIMIT_SECRET must be Base64 encoded.",
                exception);
        }

        if (_keySecret.Length < 32)
        {
            throw new InvalidOperationException(
                "API_RATE_LIMIT_SECRET must contain at least 32 bytes.");
        }
    }

    public async Task<ApiRateLimitResult>
        CheckAsync(
            string functionName,
            HttpRequest request,
            ApiRequestIdentity? identity,
            CancellationToken cancellationToken = default)
    {
        var rule =
            GetRule(
                functionName,
                identity is not null);

        var keyMaterial =
            identity is null
                ? $"ip:{ClientIPAddressResolver.GetClientIPAddress(request)}"
                : $"session:{identity.SessionID:N}";

        var rateLimitKey =
            HashKey(
                functionName,
                keyMaterial);

        var nowUtc =
            DateTimeOffset.UtcNow;

        var windowStartUtc =
            GetWindowStart(
                nowUtc,
                rule.Window);

        await using var connection =
            new SqlConnection(
                _connectionString);

        await connection.OpenAsync(
            cancellationToken);

        await using var transaction =
            (SqlTransaction)await connection
                .BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);

        const string cleanupSql = """
            DELETE TOP (500)
            FROM dbo.ApiRequestRateLimit
            WHERE WindowStartedAtUtc <
                  @CleanupBeforeUtc;
            """;

        await using (var cleanupCommand =
                     new SqlCommand(
                         cleanupSql,
                         connection,
                         transaction))
        {
            cleanupCommand.Parameters.Add(
                "@CleanupBeforeUtc",
                SqlDbType.DateTime2)
                .Value =
                    nowUtc
                        .Subtract(
                            TimeSpan.FromDays(2))
                        .UtcDateTime;

            await cleanupCommand
                .ExecuteNonQueryAsync(
                    cancellationToken);
        }

        const string selectSql = """
            SELECT
                RequestCount
            FROM dbo.ApiRequestRateLimit
                WITH (UPDLOCK, HOLDLOCK)
            WHERE RateLimitKey = @RateLimitKey
              AND FunctionName = @FunctionName
              AND WindowStartedAtUtc = @WindowStartedAtUtc;
            """;

        int currentCount;

        await using (var selectCommand =
                     new SqlCommand(
                         selectSql,
                         connection,
                         transaction))
        {
            AddKeyParameters(
                selectCommand,
                rateLimitKey,
                functionName,
                windowStartUtc);

            var result =
                await selectCommand
                    .ExecuteScalarAsync(
                        cancellationToken);

            currentCount =
                result is null ||
                result == DBNull.Value
                    ? 0
                    : Convert.ToInt32(
                        result);
        }

        if (currentCount >=
            rule.PermitLimit)
        {
            await transaction.CommitAsync(
                cancellationToken);

            return ApiRateLimitResult
                .Rejected(
                    windowStartUtc
                        .Add(
                            rule.Window) -
                    nowUtc);
        }

        const string upsertSql = """
            IF EXISTS
            (
                SELECT 1
                FROM dbo.ApiRequestRateLimit
                WHERE RateLimitKey = @RateLimitKey
                  AND FunctionName = @FunctionName
                  AND WindowStartedAtUtc = @WindowStartedAtUtc
            )
            BEGIN
                UPDATE dbo.ApiRequestRateLimit
                SET RequestCount = RequestCount + 1
                WHERE RateLimitKey = @RateLimitKey
                  AND FunctionName = @FunctionName
                  AND WindowStartedAtUtc = @WindowStartedAtUtc;
            END
            ELSE
            BEGIN
                INSERT INTO dbo.ApiRequestRateLimit
                (
                    RateLimitKey,
                    FunctionName,
                    WindowStartedAtUtc,
                    RequestCount
                )
                VALUES
                (
                    @RateLimitKey,
                    @FunctionName,
                    @WindowStartedAtUtc,
                    1
                );
            END;
            """;

        await using (var upsertCommand =
                     new SqlCommand(
                         upsertSql,
                         connection,
                         transaction))
        {
            AddKeyParameters(
                upsertCommand,
                rateLimitKey,
                functionName,
                windowStartUtc);

            await upsertCommand
                .ExecuteNonQueryAsync(
                    cancellationToken);
        }

        await transaction.CommitAsync(
            cancellationToken);

        return ApiRateLimitResult
            .Allowed();
    }

    private byte[] HashKey(
        string functionName,
        string keyMaterial)
    {
        using var hmac =
            new HMACSHA256(
                _keySecret);

        return hmac.ComputeHash(
            Encoding.UTF8.GetBytes(
                $"{functionName}\n{keyMaterial}"));
    }

    private static void AddKeyParameters(
        SqlCommand command,
        byte[] rateLimitKey,
        string functionName,
        DateTimeOffset windowStartUtc)
    {
        command.Parameters.Add(
            "@RateLimitKey",
            SqlDbType.Binary,
            32)
            .Value =
                rateLimitKey;

        command.Parameters.Add(
            "@FunctionName",
            SqlDbType.NVarChar,
            128)
            .Value =
                functionName;

        command.Parameters.Add(
            "@WindowStartedAtUtc",
            SqlDbType.DateTime2)
            .Value =
                windowStartUtc.UtcDateTime;
    }

    private static DateTimeOffset
        GetWindowStart(
            DateTimeOffset nowUtc,
            TimeSpan window)
    {
        var windowTicks =
            window.Ticks;

        return new DateTimeOffset(
            nowUtc.Ticks -
            nowUtc.Ticks %
            windowTicks,
            TimeSpan.Zero);
    }

    private static ApiRateLimitRule GetRule(
        string functionName,
        bool isAuthenticated)
    {
        if (SandboxFunctions.Contains(
                functionName))
        {
            return new ApiRateLimitRule(
                3,
                TimeSpan.FromHours(1));
        }

        if (VerificationStartFunctions
            .Contains(
                functionName))
        {
            return new ApiRateLimitRule(
                5,
                TimeSpan.FromMinutes(15));
        }

        if (VerificationFunctions.Contains(
                functionName))
        {
            return new ApiRateLimitRule(
                10,
                TimeSpan.FromMinutes(15));
        }

        if (LookupFunctions.Contains(
                functionName))
        {
            return new ApiRateLimitRule(
                20,
                TimeSpan.FromMinutes(15));
        }

        if (OutboundEmailFunctions.Contains(
                functionName))
        {
            return new ApiRateLimitRule(
                10,
                TimeSpan.FromMinutes(15));
        }

        if (PublicWriteFunctions.Contains(
                functionName))
        {
            return new ApiRateLimitRule(
                10,
                TimeSpan.FromHours(1));
        }

        if (ExpensiveFunctions.Contains(
                functionName))
        {
            return new ApiRateLimitRule(
                20,
                TimeSpan.FromMinutes(5));
        }

        if (UploadFunctions.Contains(
                functionName))
        {
            return new ApiRateLimitRule(
                10,
                TimeSpan.FromHours(1));
        }

        return isAuthenticated
            ? new ApiRateLimitRule(
                300,
                TimeSpan.FromMinutes(5))
            : new ApiRateLimitRule(
                60,
                TimeSpan.FromMinutes(15));
    }

    private sealed record ApiRateLimitRule(
        int PermitLimit,
        TimeSpan Window);
}
