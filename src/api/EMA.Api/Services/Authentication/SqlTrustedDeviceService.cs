using System.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Services.Authentication;

public sealed class SqlTrustedDeviceService :
    ITrustedDeviceService
{
    private const string CookieName =
        "EmaTrustedDevice";

    private const string LegacyCookieName =
        "TrustedDeviceToken";

    private static readonly TimeSpan TrustDuration =
        TimeSpan.FromDays(
            90);

    private readonly ILogger<SqlTrustedDeviceService>
        _logger;

    private readonly string _connectionString;

    public SqlTrustedDeviceService(
        ILogger<SqlTrustedDeviceService> logger)
    {
        _logger =
            logger;

        _connectionString =
            Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString.");
    }

    public async Task<bool> IsTrustedAsync(
        string emailAddress,
        HttpRequest request,
        CancellationToken cancellationToken = default)
    {
        string normalizedEmailAddress;

        try
        {
            normalizedEmailAddress =
                EmailAddressNormalizer.Normalize(
                    emailAddress);
        }
        catch (ArgumentException)
        {
            return false;
        }

        try
        {
            if (TryReadSecureDeviceToken(
                    request,
                    out var secureDeviceToken) &&
                await ValidateSecureDeviceAsync(
                    normalizedEmailAddress,
                    secureDeviceToken,
                    request,
                    cancellationToken))
            {
                return true;
            }

            if (TryReadLegacyDeviceToken(
                    request,
                    out var legacyDeviceToken) &&
                await TryMigrateLegacyDeviceAsync(
                    normalizedEmailAddress,
                    legacyDeviceToken,
                    request,
                    cancellationToken))
            {
                return true;
            }

            return false;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Unable to validate a trusted login device.");

            return false;
        }
    }

    public async Task RegisterSuccessfulAuthenticationAsync(
        string emailAddress,
        HttpRequest request,
        CancellationToken cancellationToken = default)
    {
        string normalizedEmailAddress;

        try
        {
            normalizedEmailAddress =
                EmailAddressNormalizer.Normalize(
                    emailAddress);
        }
        catch (ArgumentException exception)
        {
            _logger.LogWarning(
                exception,
                "Unable to trust the device because the authenticated email address is invalid.");

            return;
        }

        byte[]? existingTokenHash =
            null;

        if (TryReadSecureDeviceToken(
                request,
                out var existingToken))
        {
            existingTokenHash =
                TrustedDeviceToken.Hash(
                    existingToken);
        }

        var newToken =
            TrustedDeviceToken.Create();

        var newTokenHash =
            TrustedDeviceToken.Hash(
                newToken);

        var expiresAtUtc =
            DateTimeOffset.UtcNow.Add(
                TrustDuration);

        try
        {
            await using var connection =
                new SqlConnection(
                    _connectionString);

            await connection.OpenAsync(
                cancellationToken);

            await using var transaction =
                await connection.BeginTransactionAsync(
                    cancellationToken);

            const string sql = """
                DECLARE @NowUtc DATETIME2(7) =
                    SYSUTCDATETIME();

                IF @ExistingTokenHash IS NOT NULL
                BEGIN
                    UPDATE dbo.TrustedLoginDevice
                    SET DeviceTokenHash =
                            @NewTokenHash
                    WHERE DeviceTokenHash =
                          @ExistingTokenHash
                      AND RevokedAtUtc IS NULL
                      AND ExpiresAtUtc >
                          @NowUtc;
                END;

                UPDATE dbo.TrustedLoginDevice WITH
                (
                    UPDLOCK,
                    SERIALIZABLE
                )
                SET LastUsedAtUtc =
                        @NowUtc,
                    ExpiresAtUtc =
                        @ExpiresAtUtc,
                    RevokedAtUtc =
                        NULL
                WHERE EmailAddress =
                      @EmailAddress
                  AND DeviceTokenHash =
                      @NewTokenHash;

                IF @@ROWCOUNT = 0
                BEGIN
                    INSERT INTO dbo.TrustedLoginDevice
                    (
                        EmailAddress,
                        DeviceTokenHash,
                        DateTimeCreated,
                        LastUsedAtUtc,
                        ExpiresAtUtc,
                        RevokedAtUtc
                    )
                    VALUES
                    (
                        @EmailAddress,
                        @NewTokenHash,
                        @NowUtc,
                        @NowUtc,
                        @ExpiresAtUtc,
                        NULL
                    );
                END;
                """;

            await using var command =
                new SqlCommand(
                    sql,
                    connection,
                    (SqlTransaction)transaction);

            command.Parameters.Add(
                "@EmailAddress",
                SqlDbType.NVarChar,
                320)
                .Value =
                    normalizedEmailAddress;

            command.Parameters.Add(
                "@ExistingTokenHash",
                SqlDbType.Binary,
                32)
                .Value =
                    existingTokenHash
                    ?? (object)DBNull.Value;

            command.Parameters.Add(
                "@NewTokenHash",
                SqlDbType.Binary,
                32)
                .Value =
                    newTokenHash;

            command.Parameters.Add(
                "@ExpiresAtUtc",
                SqlDbType.DateTime2)
                .Value =
                    expiresAtUtc.UtcDateTime;

            await command.ExecuteNonQueryAsync(
                cancellationToken);

            await transaction.CommitAsync(
                cancellationToken);

            AppendTrustedDeviceCookie(
                request,
                newToken,
                expiresAtUtc);

            DeleteLegacyTrustedDeviceCookie(
                request);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Authentication succeeded, but the trusted login device could not be saved.");
        }
    }

    private async Task<bool> ValidateSecureDeviceAsync(
        string normalizedEmailAddress,
        string deviceToken,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var deviceTokenHash =
            TrustedDeviceToken.Hash(
                deviceToken);

        await using var connection =
            new SqlConnection(
                _connectionString);

        await connection.OpenAsync(
            cancellationToken);

        const string sql = """
            DECLARE @NowUtc DATETIME2(7) =
                SYSUTCDATETIME();

            UPDATE dbo.TrustedLoginDevice
            SET LastUsedAtUtc =
                @NowUtc
            OUTPUT inserted.ExpiresAtUtc
            WHERE EmailAddress =
                  @EmailAddress
              AND DeviceTokenHash =
                  @DeviceTokenHash
              AND RevokedAtUtc IS NULL
              AND ExpiresAtUtc >
                  @NowUtc;
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters.Add(
            "@EmailAddress",
            SqlDbType.NVarChar,
            320)
            .Value =
                normalizedEmailAddress;

        command.Parameters.Add(
            "@DeviceTokenHash",
            SqlDbType.Binary,
            32)
            .Value =
                deviceTokenHash;

        var result =
            await command.ExecuteScalarAsync(
                cancellationToken);

        if (result is not DateTime
            expiresAt)
        {
            return false;
        }

        AppendTrustedDeviceCookie(
            request,
            deviceToken,
            new DateTimeOffset(
                DateTime.SpecifyKind(
                    expiresAt,
                    DateTimeKind.Utc)));

        DeleteLegacyTrustedDeviceCookie(
            request);

        return true;
    }

    private async Task<bool> TryMigrateLegacyDeviceAsync(
        string normalizedEmailAddress,
        Guid legacyDeviceToken,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var newToken =
            TrustedDeviceToken.Create();

        var newTokenHash =
            TrustedDeviceToken.Hash(
                newToken);

        var expiresAtUtc =
            DateTimeOffset.UtcNow.Add(
                TrustDuration);

        await using var connection =
            new SqlConnection(
                _connectionString);

        await connection.OpenAsync(
            cancellationToken);

        const string requiredTablesSql = """
            SELECT CASE
                WHEN OBJECT_ID(
                         N'dbo.TrustedLogins',
                         N'U') IS NOT NULL
                 AND OBJECT_ID(
                         N'dbo.TrustedLoginDevice',
                         N'U') IS NOT NULL
                    THEN CAST(1 AS BIT)
                ELSE CAST(0 AS BIT)
            END;
            """;

        await using (var requiredTablesCommand =
                     new SqlCommand(
                         requiredTablesSql,
                         connection))
        {
            var requiredTablesExist =
                await requiredTablesCommand
                    .ExecuteScalarAsync(
                        cancellationToken);

            if (requiredTablesExist is not bool
                tablesExist ||
                !tablesExist)
            {
                return false;
            }
        }

        await using var transaction =
            await connection.BeginTransactionAsync(
                cancellationToken);

        const string migrationSql = """
            DECLARE @NowUtc DATETIME2(7) =
                SYSUTCDATETIME();

            DECLARE @LegacyCutoffUtc DATETIME2(7) =
                DATEADD(
                    DAY,
                    -90,
                    @NowUtc);

            IF NOT EXISTS
            (
                SELECT 1
                FROM dbo.TrustedLogins WITH
                (
                    UPDLOCK,
                    HOLDLOCK
                )
                WHERE LOWER(
                          LTRIM(
                              RTRIM(
                                  EmailAddress))) =
                      @EmailAddress
                  AND DeviceToken =
                      @LegacyDeviceToken
                  AND DateTimeUpdated >=
                      @LegacyCutoffUtc
            )
            BEGIN
                SELECT CAST(0 AS BIT);
                RETURN;
            END;

            UPDATE dbo.TrustedLoginDevice WITH
            (
                UPDLOCK,
                SERIALIZABLE
            )
            SET LastUsedAtUtc =
                    @NowUtc,
                ExpiresAtUtc =
                    @ExpiresAtUtc,
                RevokedAtUtc =
                    NULL
            WHERE EmailAddress =
                  @EmailAddress
              AND DeviceTokenHash =
                  @NewTokenHash;

            IF @@ROWCOUNT = 0
            BEGIN
                INSERT INTO dbo.TrustedLoginDevice
                (
                    EmailAddress,
                    DeviceTokenHash,
                    DateTimeCreated,
                    LastUsedAtUtc,
                    ExpiresAtUtc,
                    RevokedAtUtc
                )
                VALUES
                (
                    @EmailAddress,
                    @NewTokenHash,
                    @NowUtc,
                    @NowUtc,
                    @ExpiresAtUtc,
                    NULL
                );
            END;

            DELETE FROM dbo.TrustedLogins
            WHERE LOWER(
                      LTRIM(
                          RTRIM(
                              EmailAddress))) =
                  @EmailAddress
              AND DeviceToken =
                  @LegacyDeviceToken;

            SELECT CAST(1 AS BIT);
            """;

        await using var migrationCommand =
            new SqlCommand(
                migrationSql,
                connection,
                (SqlTransaction)transaction);

        migrationCommand.Parameters.Add(
            "@EmailAddress",
            SqlDbType.NVarChar,
            320)
            .Value =
                normalizedEmailAddress;

        migrationCommand.Parameters.Add(
            "@LegacyDeviceToken",
            SqlDbType.UniqueIdentifier)
            .Value =
                legacyDeviceToken;

        migrationCommand.Parameters.Add(
            "@NewTokenHash",
            SqlDbType.Binary,
            32)
            .Value =
                newTokenHash;

        migrationCommand.Parameters.Add(
            "@ExpiresAtUtc",
            SqlDbType.DateTime2)
            .Value =
                expiresAtUtc.UtcDateTime;

        var migrationResult =
            await migrationCommand.ExecuteScalarAsync(
                cancellationToken);

        var wasMigrated =
            migrationResult is bool
            migrated &&
            migrated;

        await transaction.CommitAsync(
            cancellationToken);

        if (!wasMigrated)
        {
            return false;
        }

        AppendTrustedDeviceCookie(
            request,
            newToken,
            expiresAtUtc);

        DeleteLegacyTrustedDeviceCookie(
            request);

        _logger.LogInformation(
            "Migrated a validated legacy trusted-device token to the secure trusted-device model for {EmailAddress}.",
            normalizedEmailAddress);

        return true;
    }

    private static bool TryReadSecureDeviceToken(
        HttpRequest request,
        out string deviceToken)
    {
        deviceToken =
            string.Empty;

        if (!request.Cookies.TryGetValue(
                CookieName,
                out var token) ||
            !TrustedDeviceToken.IsWellFormed(
                token))
        {
            return false;
        }

        deviceToken =
            token;

        return true;
    }

    private static bool TryReadLegacyDeviceToken(
        HttpRequest request,
        out Guid deviceToken)
    {
        deviceToken =
            Guid.Empty;

        return request.Cookies.TryGetValue(
                   LegacyCookieName,
                   out var token) &&
               Guid.TryParse(
                   token,
                   out deviceToken) &&
               deviceToken != Guid.Empty;
    }

    private static void AppendTrustedDeviceCookie(
        HttpRequest request,
        string deviceToken,
        DateTimeOffset expiresAtUtc)
    {
        var maxAge =
            expiresAtUtc -
            DateTimeOffset.UtcNow;

        if (maxAge <= TimeSpan.Zero)
        {
            return;
        }

        var useSecureCookie =
            ShouldUseSecureCookie(
                request);

        request.HttpContext.Response.Cookies.Append(
            CookieName,
            deviceToken,
            new CookieOptions
            {
                HttpOnly = true,
                Secure =
                    useSecureCookie,
                SameSite =
                    useSecureCookie
                        ? SameSiteMode.None
                        : SameSiteMode.Lax,
                IsEssential = true,
                Path = "/",
                Expires =
                    expiresAtUtc,
                MaxAge =
                    maxAge
            });
    }

    private static void DeleteLegacyTrustedDeviceCookie(
        HttpRequest request)
    {
        if (!request.Cookies.ContainsKey(
                LegacyCookieName))
        {
            return;
        }

        var useSecureCookie =
            ShouldUseSecureCookie(
                request);

        request.HttpContext.Response.Cookies.Delete(
            LegacyCookieName,
            new CookieOptions
            {
                HttpOnly = true,
                Secure =
                    useSecureCookie,
                SameSite =
                    useSecureCookie
                        ? SameSiteMode.None
                        : SameSiteMode.Lax,
                IsEssential = true,
                Path = "/"
            });
    }

    private static bool ShouldUseSecureCookie(
        HttpRequest request)
    {
        if (request.IsHttps)
        {
            return true;
        }

        var forwardedProtocol =
            request.Headers[
                    "X-Forwarded-Proto"]
                .FirstOrDefault();

        if (string.Equals(
                forwardedProtocol,
                "https",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var host =
            request.Host.Host;

        return !string.Equals(
                   host,
                   "localhost",
                   StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(
                   host,
                   "127.0.0.1",
                   StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(
                   host,
                   "::1",
                   StringComparison.OrdinalIgnoreCase);
    }
}
