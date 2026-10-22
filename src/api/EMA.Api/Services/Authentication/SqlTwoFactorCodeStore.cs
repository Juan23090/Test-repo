using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace EMA.Api.Services.Authentication;

public sealed class SqlTwoFactorCodeStore : ITwoFactorCodeStore
{
    private static readonly TimeSpan CodeLifetime =
        TimeSpan.FromMinutes(5);

    private static readonly TimeSpan ResendCooldown =
        TimeSpan.FromSeconds(60);

    private static readonly TimeSpan RateLimitWindow =
        TimeSpan.FromMinutes(15);

    private const int MaxCodesPerWindow = 5;
    private const int MaxCodesPerDestinationWindow = 10;
    private const int MaxFailedAttempts = 5;

    private readonly string _connectionString;
    private readonly byte[] _codeSecret;
    private readonly int _globalMaxCodesPerWindow;

    public SqlTwoFactorCodeStore(IConfiguration configuration)
    {
        _connectionString =
            configuration["SqlConnectionString"]
            ?? Environment.GetEnvironmentVariable("SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString.");

        var configuredSecret =
            configuration["TWO_FACTOR_CODE_SECRET"]
            ?? Environment.GetEnvironmentVariable("TWO_FACTOR_CODE_SECRET");

        if (string.IsNullOrWhiteSpace(configuredSecret) ||
            configuredSecret.Length < 32)
        {
            throw new InvalidOperationException(
                "TWO_FACTOR_CODE_SECRET must be configured with at least 32 characters.");
        }

        _codeSecret = Encoding.UTF8.GetBytes(configuredSecret);

        var configuredGlobalLimit =
            configuration[
                "TWO_FACTOR_GLOBAL_MAX_CODES_PER_15_MINUTES"];

        if (!int.TryParse(
                configuredGlobalLimit,
                out var globalLimit) ||
            globalLimit <= 0)
        {
            if (ApiRuntimeEnvironment
                    .IsProductionLike)
            {
                throw new InvalidOperationException(
                    "TWO_FACTOR_GLOBAL_MAX_CODES_PER_15_MINUTES must be a positive integer in production.");
            }

            _globalMaxCodesPerWindow =
                500;
        }
        else
        {
            _globalMaxCodesPerWindow =
                globalLimit;
        }
    }

    public async Task<TwoFactorCodeRecord> CreateCodeAsync(
        TwoFactorPurpose purpose,
        int actorID,
        string emailAddress,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail =
            EmailAddressNormalizer.Normalize(emailAddress);

        var destinationHash =
            HashDestination(normalizedEmail);

        var verificationID =
            Guid.NewGuid().ToString("N");

        var code =
            RandomNumberGenerator
                .GetInt32(100000, 1000000)
                .ToString();

        var now =
            DateTimeOffset.UtcNow;

        var expiresAtUtc =
            now.Add(CodeLifetime);

        var codeHash =
            HashCode(verificationID, code);

        await using var connection =
            new SqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        const string cleanupSql = """
            DELETE TOP (200)
            FROM dbo.VerificationChallenge
            WHERE ExpiresAtUtc < @CleanupBeforeUtc;
            """;

        await using (var cleanupCommand =
            new SqlCommand(
                cleanupSql,
                connection,
                transaction))
        {
            cleanupCommand.Parameters
                .Add(
                    "@CleanupBeforeUtc",
                    SqlDbType.DateTimeOffset)
                .Value =
                    now.Subtract(TimeSpan.FromDays(1));

            await cleanupCommand.ExecuteNonQueryAsync(
                cancellationToken);
        }

        const string recentSql = """
            SELECT
                COUNT_BIG(*) AS CodeCount,
                MAX(CreatedAtUtc) AS LastCreatedAtUtc
            FROM dbo.VerificationChallenge WITH (UPDLOCK, HOLDLOCK)
            WHERE Purpose = @Purpose
              AND ActorID = @ActorID
              AND DestinationHash = @DestinationHash
              AND CreatedAtUtc >= @WindowStartUtc;
            """;

        await using (var recentCommand =
            new SqlCommand(
                recentSql,
                connection,
                transaction))
        {
            recentCommand.Parameters
                .Add(
                    "@Purpose",
                    SqlDbType.Int)
                .Value =
                    (int)purpose;

            recentCommand.Parameters
                .Add(
                    "@ActorID",
                    SqlDbType.Int)
                .Value =
                    actorID;

            recentCommand.Parameters
                .Add(
                    "@DestinationHash",
                    SqlDbType.VarBinary,
                    32)
                .Value =
                    destinationHash;

            recentCommand.Parameters
                .Add(
                    "@WindowStartUtc",
                    SqlDbType.DateTimeOffset)
                .Value =
                    now.Subtract(RateLimitWindow);

            await using var reader =
                await recentCommand.ExecuteReaderAsync(
                    cancellationToken);

            await reader.ReadAsync(cancellationToken);

            var codeCount =
                Convert.ToInt64(reader["CodeCount"]);

            var lastCreatedAtUtc =
                reader["LastCreatedAtUtc"] == DBNull.Value
                    ? (DateTimeOffset?)null
                    : (DateTimeOffset)reader["LastCreatedAtUtc"];

            await reader.CloseAsync();

            if (lastCreatedAtUtc.HasValue)
            {
                var elapsed =
                    now - lastCreatedAtUtc.Value;

                if (elapsed < ResendCooldown)
                {
                    throw new TwoFactorCodeRateLimitException(
                        "Please wait before requesting another verification code.",
                        ResendCooldown - elapsed);
                }
            }

            if (codeCount >= MaxCodesPerWindow)
            {
                throw new TwoFactorCodeRateLimitException(
                    "Too many verification codes have been requested. Please try again later.",
                    RateLimitWindow);
            }
        }

        const string destinationRateLimitSql = """
            SELECT COUNT_BIG(*)
            FROM dbo.VerificationChallenge WITH (UPDLOCK, HOLDLOCK)
            WHERE DestinationHash = @DestinationHash
              AND CreatedAtUtc >= @WindowStartUtc;
            """;

        await using (var destinationRateLimitCommand =
            new SqlCommand(
                destinationRateLimitSql,
                connection,
                transaction))
        {
            destinationRateLimitCommand.Parameters
                .Add(
                    "@DestinationHash",
                    SqlDbType.VarBinary,
                    32)
                .Value =
                    destinationHash;

            destinationRateLimitCommand.Parameters
                .Add(
                    "@WindowStartUtc",
                    SqlDbType.DateTimeOffset)
                .Value =
                    now.Subtract(RateLimitWindow);

            var destinationCodeCount =
                Convert.ToInt64(
                    await destinationRateLimitCommand
                        .ExecuteScalarAsync(
                            cancellationToken));

            if (destinationCodeCount >=
                MaxCodesPerDestinationWindow)
            {
                throw new TwoFactorCodeRateLimitException(
                    "Too many verification codes have been sent to this email address. Please try again later.",
                    RateLimitWindow);
            }
        }

        const string globalRateLimitSql = """
            SELECT COUNT_BIG(*)
            FROM dbo.VerificationChallenge
                WITH (UPDLOCK, HOLDLOCK)
            WHERE CreatedAtUtc >= @WindowStartUtc;
            """;

        await using (var globalRateLimitCommand =
                     new SqlCommand(
                         globalRateLimitSql,
                         connection,
                         transaction))
        {
            globalRateLimitCommand.Parameters
                .Add(
                    "@WindowStartUtc",
                    SqlDbType.DateTimeOffset)
                .Value =
                    now.Subtract(
                        RateLimitWindow);

            var globalCodeCount =
                Convert.ToInt64(
                    await globalRateLimitCommand
                        .ExecuteScalarAsync(
                            cancellationToken));

            if (globalCodeCount >=
                _globalMaxCodesPerWindow)
            {
                throw new TwoFactorCodeRateLimitException(
                    "Verification service capacity has been reached. Please try again later.",
                    RateLimitWindow);
            }
        }

        const string revokeSql = """
            UPDATE dbo.VerificationChallenge
            SET ConsumedAtUtc = @NowUtc
            WHERE Purpose = @Purpose
              AND ActorID = @ActorID
              AND DestinationHash = @DestinationHash
              AND ConsumedAtUtc IS NULL;
            """;

        await using (var revokeCommand =
            new SqlCommand(
                revokeSql,
                connection,
                transaction))
        {
            revokeCommand.Parameters
                .Add(
                    "@NowUtc",
                    SqlDbType.DateTimeOffset)
                .Value =
                    now;

            revokeCommand.Parameters
                .Add(
                    "@Purpose",
                    SqlDbType.Int)
                .Value =
                    (int)purpose;

            revokeCommand.Parameters
                .Add(
                    "@ActorID",
                    SqlDbType.Int)
                .Value =
                    actorID;

            revokeCommand.Parameters
                .Add(
                    "@DestinationHash",
                    SqlDbType.VarBinary,
                    32)
                .Value =
                    destinationHash;

            await revokeCommand.ExecuteNonQueryAsync(
                cancellationToken);
        }

        const string insertSql = """
            INSERT INTO dbo.VerificationChallenge
            (
                VerificationID,
                Purpose,
                ActorID,
                DestinationHash,
                CodeHash,
                FailedAttempts,
                CreatedAtUtc,
                ExpiresAtUtc
            )
            VALUES
            (
                @VerificationID,
                @Purpose,
                @ActorID,
                @DestinationHash,
                @CodeHash,
                0,
                @CreatedAtUtc,
                @ExpiresAtUtc
            );
            """;

        await using (var insertCommand =
            new SqlCommand(
                insertSql,
                connection,
                transaction))
        {
            insertCommand.Parameters
                .Add(
                    "@VerificationID",
                    SqlDbType.Char,
                    32)
                .Value =
                    verificationID;

            insertCommand.Parameters
                .Add(
                    "@Purpose",
                    SqlDbType.Int)
                .Value =
                    (int)purpose;

            insertCommand.Parameters
                .Add(
                    "@ActorID",
                    SqlDbType.Int)
                .Value =
                    actorID;

            insertCommand.Parameters
                .Add(
                    "@DestinationHash",
                    SqlDbType.VarBinary,
                    32)
                .Value =
                    destinationHash;

            insertCommand.Parameters
                .Add(
                    "@CodeHash",
                    SqlDbType.VarBinary,
                    32)
                .Value =
                    codeHash;

            insertCommand.Parameters
                .Add(
                    "@CreatedAtUtc",
                    SqlDbType.DateTimeOffset)
                .Value =
                    now;

            insertCommand.Parameters
                .Add(
                    "@ExpiresAtUtc",
                    SqlDbType.DateTimeOffset)
                .Value =
                    expiresAtUtc;

            await insertCommand.ExecuteNonQueryAsync(
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return new TwoFactorCodeRecord
        {
            VerificationID = verificationID,
            Code = code,
            ExpiresAtUtc = expiresAtUtc
        };
    }

    public async Task<bool> VerifyCodeAsync(
        string verificationID,
        TwoFactorPurpose purpose,
        int actorID,
        string? emailAddress,
        string code,
        bool consumeOnSuccess = true,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidVerificationID(verificationID) ||
            string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        byte[]? expectedDestinationHash = null;

        if (!string.IsNullOrWhiteSpace(emailAddress))
        {
            expectedDestinationHash =
                HashDestination(
                    EmailAddressNormalizer.Normalize(
                        emailAddress));
        }

        var now =
            DateTimeOffset.UtcNow;

        await using var connection =
            new SqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);

        const string selectSql = """
            SELECT
                DestinationHash,
                CodeHash,
                FailedAttempts,
                ExpiresAtUtc,
                VerifiedAtUtc,
                ConsumedAtUtc
            FROM dbo.VerificationChallenge WITH (UPDLOCK, ROWLOCK)
            WHERE VerificationID = @VerificationID
              AND Purpose = @Purpose
              AND ActorID = @ActorID;
            """;

        byte[] storedDestinationHash;
        byte[] storedCodeHash;
        int failedAttempts;
        DateTimeOffset expiresAtUtc;
        DateTimeOffset? verifiedAtUtc;
        DateTimeOffset? consumedAtUtc;

        await using (var selectCommand =
            new SqlCommand(
                selectSql,
                connection,
                transaction))
        {
            selectCommand.Parameters
                .Add(
                    "@VerificationID",
                    SqlDbType.Char,
                    32)
                .Value =
                    verificationID.Trim();

            selectCommand.Parameters
                .Add(
                    "@Purpose",
                    SqlDbType.Int)
                .Value =
                    (int)purpose;

            selectCommand.Parameters
                .Add(
                    "@ActorID",
                    SqlDbType.Int)
                .Value =
                    actorID;

            await using var reader =
                await selectCommand.ExecuteReaderAsync(
                    cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return false;
            }

            storedDestinationHash =
                (byte[])reader["DestinationHash"];

            storedCodeHash =
                (byte[])reader["CodeHash"];

            failedAttempts =
                Convert.ToInt32(
                    reader["FailedAttempts"]);

            expiresAtUtc =
                (DateTimeOffset)reader["ExpiresAtUtc"];

            verifiedAtUtc =
                reader["VerifiedAtUtc"] == DBNull.Value
                    ? null
                    : (DateTimeOffset)reader["VerifiedAtUtc"];

            consumedAtUtc =
                reader["ConsumedAtUtc"] == DBNull.Value
                    ? null
                    : (DateTimeOffset)reader["ConsumedAtUtc"];
        }

        if (consumedAtUtc.HasValue ||
            verifiedAtUtc.HasValue ||
            expiresAtUtc <= now ||
            failedAttempts >= MaxFailedAttempts ||
            (expectedDestinationHash is not null &&
             !CryptographicOperations.FixedTimeEquals(
                 storedDestinationHash,
                 expectedDestinationHash)))
        {
            await transaction.RollbackAsync(
                cancellationToken);

            return false;
        }

        var suppliedCodeHash =
            HashCode(
                verificationID.Trim(),
                code.Trim());

        var matches =
            CryptographicOperations.FixedTimeEquals(
                storedCodeHash,
                suppliedCodeHash);

        if (!matches)
        {
            var nextFailedAttempts =
                failedAttempts + 1;

            const string failedSql = """
                UPDATE dbo.VerificationChallenge
                SET
                    FailedAttempts = @FailedAttempts,
                    ConsumedAtUtc = CASE
                        WHEN @FailedAttempts >= @MaxFailedAttempts
                            THEN @NowUtc
                        ELSE ConsumedAtUtc
                    END
                WHERE VerificationID = @VerificationID;
                """;

            await using var failedCommand =
                new SqlCommand(
                    failedSql,
                    connection,
                    transaction);

            failedCommand.Parameters
                .Add(
                    "@FailedAttempts",
                    SqlDbType.Int)
                .Value =
                    nextFailedAttempts;

            failedCommand.Parameters
                .Add(
                    "@MaxFailedAttempts",
                    SqlDbType.Int)
                .Value =
                    MaxFailedAttempts;

            failedCommand.Parameters
                .Add(
                    "@NowUtc",
                    SqlDbType.DateTimeOffset)
                .Value =
                    now;

            failedCommand.Parameters
                .Add(
                    "@VerificationID",
                    SqlDbType.Char,
                    32)
                .Value =
                    verificationID.Trim();

            await failedCommand.ExecuteNonQueryAsync(
                cancellationToken);

            await transaction.CommitAsync(
                cancellationToken);

            return false;
        }

        const string verifiedSql = """
            UPDATE dbo.VerificationChallenge
            SET
                VerifiedAtUtc = @NowUtc,
                ConsumedAtUtc = CASE
                    WHEN @ConsumeOnSuccess = 1
                        THEN @NowUtc
                    ELSE NULL
                END
            WHERE VerificationID = @VerificationID;
            """;

        await using (var verifiedCommand =
            new SqlCommand(
                verifiedSql,
                connection,
                transaction))
        {
            verifiedCommand.Parameters
                .Add(
                    "@NowUtc",
                    SqlDbType.DateTimeOffset)
                .Value =
                    now;

            verifiedCommand.Parameters
                .Add(
                    "@ConsumeOnSuccess",
                    SqlDbType.Bit)
                .Value =
                    consumeOnSuccess;

            verifiedCommand.Parameters
                .Add(
                    "@VerificationID",
                    SqlDbType.Char,
                    32)
                .Value =
                    verificationID.Trim();

            await verifiedCommand.ExecuteNonQueryAsync(
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    public async Task<bool> ConsumeVerifiedCodeAsync(
        string verificationID,
        TwoFactorPurpose purpose,
        int actorID,
        string emailAddress,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidVerificationID(verificationID))
        {
            return false;
        }

        var destinationHash =
            HashDestination(
                EmailAddressNormalizer.Normalize(
                    emailAddress));

        const string sql = """
            UPDATE dbo.VerificationChallenge
            SET ConsumedAtUtc = @NowUtc
            WHERE VerificationID = @VerificationID
              AND Purpose = @Purpose
              AND ActorID = @ActorID
              AND DestinationHash = @DestinationHash
              AND VerifiedAtUtc IS NOT NULL
              AND ConsumedAtUtc IS NULL
              AND ExpiresAtUtc > @NowUtc;
            """;

        await using var connection =
            new SqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command =
            new SqlCommand(sql, connection);

        var now =
            DateTimeOffset.UtcNow;

        command.Parameters
            .Add(
                "@NowUtc",
                SqlDbType.DateTimeOffset)
            .Value =
                now;

        command.Parameters
            .Add(
                "@VerificationID",
                SqlDbType.Char,
                32)
            .Value =
                verificationID.Trim();

        command.Parameters
            .Add(
                "@Purpose",
                SqlDbType.Int)
            .Value =
                (int)purpose;

        command.Parameters
            .Add(
                "@ActorID",
                SqlDbType.Int)
            .Value =
                actorID;

        command.Parameters
            .Add(
                "@DestinationHash",
                SqlDbType.VarBinary,
                32)
            .Value =
                destinationHash;

        return await command.ExecuteNonQueryAsync(
            cancellationToken) == 1;
    }

    public async Task RevokeCodeAsync(
        string verificationID,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidVerificationID(verificationID))
        {
            return;
        }

        const string sql = """
            DELETE FROM dbo.VerificationChallenge
            WHERE VerificationID = @VerificationID;
            """;

        await using var connection =
            new SqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command =
            new SqlCommand(sql, connection);

        command.Parameters
            .Add(
                "@VerificationID",
                SqlDbType.Char,
                32)
            .Value =
                verificationID.Trim();

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private byte[] HashCode(
        string verificationID,
        string code)
    {
        using var hmac =
            new HMACSHA256(_codeSecret);

        return hmac.ComputeHash(
            Encoding.UTF8.GetBytes(
                $"{verificationID}:{code}"));
    }

    private static byte[] HashDestination(
        string normalizedEmailAddress) =>
        SHA256.HashData(
            Encoding.UTF8.GetBytes(
                normalizedEmailAddress));

    private static bool IsValidVerificationID(
        string? verificationID) =>
        !string.IsNullOrWhiteSpace(verificationID) &&
        verificationID.Length == 32 &&
        Guid.TryParseExact(
            verificationID,
            "N",
            out _);
}
