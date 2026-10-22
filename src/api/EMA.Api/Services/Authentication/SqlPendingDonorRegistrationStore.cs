using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace EMA.Api.Services.Authentication;

public sealed class SqlPendingDonorRegistrationStore :
    IPendingDonorRegistrationStore
{
    private static readonly TimeSpan
        RegistrationLifetime =
            TimeSpan.FromMinutes(10);

    private static readonly TimeSpan
        RegistrationWindow =
            TimeSpan.FromHours(1);

    private const int
        MaxRegistrationsPerDestination = 5;

    private const int MaxFailedAttempts = 5;

    private readonly string _connectionString;
    private readonly byte[] _codeKey;
    private readonly byte[] _encryptionKey;
    private readonly byte[] _destinationKey;

    public SqlPendingDonorRegistrationStore(
        IConfiguration configuration)
    {
        _connectionString =
            configuration["SqlConnectionString"]
            ?? Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString.");

        var configuredSecret =
            configuration[
                "PENDING_REGISTRATION_SECRET"];

        byte[] masterKey;

        try
        {
            masterKey =
                Convert.FromBase64String(
                    configuredSecret
                    ?? string.Empty);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "PENDING_REGISTRATION_SECRET must be Base64 encoded.",
                exception);
        }

        if (masterKey.Length < 32)
        {
            throw new InvalidOperationException(
                "PENDING_REGISTRATION_SECRET must contain at least 32 bytes.");
        }

        _codeKey =
            DeriveKey(
                masterKey,
                "registration-code");

        _encryptionKey =
            DeriveKey(
                masterKey,
                "registration-data");

        _destinationKey =
            DeriveKey(
                masterKey,
                "registration-destination");
    }

    public async Task<
        PendingDonorRegistrationRecord>
        CreateRegistrationAsync(
            int EMAOrganizationID,
            string officialDonorName,
            string? interacDonorName,
            string address,
            string city,
            string provinceStateCode,
            string postalCode,
            string country,
            string phoneNumber,
            string emailAddress,
            CancellationToken cancellationToken = default)
    {
        var nowUtc =
            DateTimeOffset.UtcNow;

        var record =
            new PendingDonorRegistrationRecord
            {
                RegistrationID =
                    Guid.NewGuid()
                        .ToString("N"),

                Code =
                    RandomNumberGenerator
                        .GetInt32(
                            100000,
                            1000000)
                        .ToString(),

                EMAOrganizationID =
                    EMAOrganizationID,

                OfficialDonorName =
                    officialDonorName,

                InteracDonorName =
                    interacDonorName,

                Address =
                    address,

                City =
                    city,

                ProvinceStateCode =
                    provinceStateCode,

                PostalCode =
                    postalCode,

                Country =
                    country,

                PhoneNumber =
                    phoneNumber,

                EmailAddress =
                    emailAddress,

                ExpiresAtUtc =
                    nowUtc.Add(
                        RegistrationLifetime)
            };

        var payload =
            JsonSerializer.SerializeToUtf8Bytes(
                new RegistrationPayload(
                    record.EMAOrganizationID,
                    record.OfficialDonorName,
                    record.InteracDonorName,
                    record.Address,
                    record.City,
                    record.ProvinceStateCode,
                    record.PostalCode,
                    record.Country,
                    record.PhoneNumber,
                    record.EmailAddress));

        var nonce =
            RandomNumberGenerator.GetBytes(
                12);

        var encryptedPayload =
            new byte[payload.Length];

        var authenticationTag =
            new byte[16];

        using (var aes =
               new AesGcm(
                   _encryptionKey,
                   authenticationTag.Length))
        {
            aes.Encrypt(
                nonce,
                payload,
                encryptedPayload,
                authenticationTag,
                Encoding.UTF8.GetBytes(
                    record.RegistrationID));
        }

        var codeHash =
            HashCode(
                record.RegistrationID,
                record.Code);

        var destinationHash =
            HashDestination(
                emailAddress);

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

        const string recentSql = """
            SELECT COUNT_BIG(*)
            FROM dbo.PendingDonorRegistration
                WITH (UPDLOCK, HOLDLOCK)
            WHERE DestinationHash = @DestinationHash
              AND CreatedAtUtc >= @WindowStartUtc;
            """;

        await using (var recentCommand =
                     new SqlCommand(
                         recentSql,
                         connection,
                         transaction))
        {
            recentCommand.Parameters.Add(
                "@DestinationHash",
                SqlDbType.Binary,
                32)
                .Value =
                    destinationHash;

            recentCommand.Parameters.Add(
                "@WindowStartUtc",
                SqlDbType.DateTime2)
                .Value =
                    nowUtc.Subtract(
                            RegistrationWindow)
                        .UtcDateTime;

            var recentCount =
                Convert.ToInt64(
                    await recentCommand
                        .ExecuteScalarAsync(
                            cancellationToken));

            if (recentCount >=
                MaxRegistrationsPerDestination)
            {
                throw new TwoFactorCodeRateLimitException(
                    "Too many donor registration codes have been requested. Please try again later.",
                    RegistrationWindow);
            }
        }

        const string insertSql = """
            INSERT INTO dbo.PendingDonorRegistration
            (
                RegistrationID,
                EMAOrganizationID,
                DestinationHash,
                CodeHash,
                EncryptedPayload,
                EncryptionNonce,
                AuthenticationTag,
                FailedAttempts,
                CreatedAtUtc,
                ExpiresAtUtc,
                ConsumedAtUtc
            )
            VALUES
            (
                @RegistrationID,
                @EMAOrganizationID,
                @DestinationHash,
                @CodeHash,
                @EncryptedPayload,
                @EncryptionNonce,
                @AuthenticationTag,
                0,
                @CreatedAtUtc,
                @ExpiresAtUtc,
                NULL
            );
            """;

        await using (var insertCommand =
                     new SqlCommand(
                         insertSql,
                         connection,
                         transaction))
        {
            insertCommand.Parameters.Add(
                "@RegistrationID",
                SqlDbType.Char,
                32)
                .Value =
                    record.RegistrationID;

            insertCommand.Parameters.Add(
                "@EMAOrganizationID",
                SqlDbType.Int)
                .Value =
                    record.EMAOrganizationID;

            insertCommand.Parameters.Add(
                "@DestinationHash",
                SqlDbType.Binary,
                32)
                .Value =
                    destinationHash;

            insertCommand.Parameters.Add(
                "@CodeHash",
                SqlDbType.Binary,
                32)
                .Value =
                    codeHash;

            insertCommand.Parameters.Add(
                "@EncryptedPayload",
                SqlDbType.VarBinary,
                -1)
                .Value =
                    encryptedPayload;

            insertCommand.Parameters.Add(
                "@EncryptionNonce",
                SqlDbType.Binary,
                12)
                .Value =
                    nonce;

            insertCommand.Parameters.Add(
                "@AuthenticationTag",
                SqlDbType.Binary,
                16)
                .Value =
                    authenticationTag;

            insertCommand.Parameters.Add(
                "@CreatedAtUtc",
                SqlDbType.DateTime2)
                .Value =
                    nowUtc.UtcDateTime;

            insertCommand.Parameters.Add(
                "@ExpiresAtUtc",
                SqlDbType.DateTime2)
                .Value =
                    record
                        .ExpiresAtUtc
                        .UtcDateTime;

            await insertCommand
                .ExecuteNonQueryAsync(
                    cancellationToken);
        }

        await transaction.CommitAsync(
            cancellationToken);

        CryptographicOperations.ZeroMemory(
            payload);

        return record;
    }

    public async Task<
        PendingDonorRegistrationRecord?>
        TryVerifyAndConsumeAsync(
            string registrationID,
            string code,
            CancellationToken cancellationToken = default)
    {
        if (!IsValidRegistrationID(
                registrationID) ||
            string.IsNullOrWhiteSpace(
                code))
        {
            return null;
        }

        registrationID =
            registrationID.Trim()
                .ToLowerInvariant();

        var nowUtc =
            DateTimeOffset.UtcNow;

        await using var connection =
            new SqlConnection(
                _connectionString);

        await connection.OpenAsync(
            cancellationToken);

        await using var transaction =
            (SqlTransaction)await connection
                .BeginTransactionAsync(
                    IsolationLevel.ReadCommitted,
                    cancellationToken);

        const string selectSql = """
            SELECT
                EMAOrganizationID,
                CodeHash,
                EncryptedPayload,
                EncryptionNonce,
                AuthenticationTag,
                FailedAttempts,
                ExpiresAtUtc,
                ConsumedAtUtc
            FROM dbo.PendingDonorRegistration
                WITH (UPDLOCK, ROWLOCK)
            WHERE RegistrationID =
                  @RegistrationID;
            """;

        byte[] codeHash;
        byte[] encryptedPayload;
        byte[] nonce;
        byte[] authenticationTag;
        int EMAOrganizationID;
        int failedAttempts;
        DateTime expiresAtUtc;
        DateTime? consumedAtUtc;

        await using (var selectCommand =
                     new SqlCommand(
                         selectSql,
                         connection,
                         transaction))
        {
            selectCommand.Parameters.Add(
                "@RegistrationID",
                SqlDbType.Char,
                32)
                .Value =
                    registrationID;

            await using var reader =
                await selectCommand
                    .ExecuteReaderAsync(
                        cancellationToken);

            if (!await reader.ReadAsync(
                    cancellationToken))
            {
                await transaction
                    .CommitAsync(
                        cancellationToken);

                return null;
            }

            EMAOrganizationID =
                reader.GetInt32(
                    reader.GetOrdinal(
                        "EMAOrganizationID"));

            codeHash =
                (byte[])reader[
                    "CodeHash"];

            encryptedPayload =
                (byte[])reader[
                    "EncryptedPayload"];

            nonce =
                (byte[])reader[
                    "EncryptionNonce"];

            authenticationTag =
                (byte[])reader[
                    "AuthenticationTag"];

            failedAttempts =
                reader.GetInt32(
                    reader.GetOrdinal(
                        "FailedAttempts"));

            expiresAtUtc =
                reader.GetDateTime(
                    reader.GetOrdinal(
                        "ExpiresAtUtc"));

            consumedAtUtc =
                reader.IsDBNull(
                    reader.GetOrdinal(
                        "ConsumedAtUtc"))
                    ? null
                    : reader.GetDateTime(
                        reader.GetOrdinal(
                            "ConsumedAtUtc"));
        }

        var suppliedCodeHash =
            HashCode(
                registrationID,
                code.Trim());

        var isValid =
            consumedAtUtc is null &&
            expiresAtUtc > nowUtc.UtcDateTime &&
            failedAttempts <
                MaxFailedAttempts &&
            CryptographicOperations
                .FixedTimeEquals(
                    suppliedCodeHash,
                    codeHash);

        if (!isValid)
        {
            const string failSql = """
                UPDATE dbo.PendingDonorRegistration
                SET FailedAttempts =
                    CASE
                        WHEN FailedAttempts <
                             @MaxFailedAttempts
                        THEN FailedAttempts + 1
                        ELSE FailedAttempts
                    END
                WHERE RegistrationID =
                      @RegistrationID
                  AND ConsumedAtUtc IS NULL;
                """;

            await using var failCommand =
                new SqlCommand(
                    failSql,
                    connection,
                    transaction);

            failCommand.Parameters.Add(
                "@MaxFailedAttempts",
                SqlDbType.Int)
                .Value =
                    MaxFailedAttempts;

            failCommand.Parameters.Add(
                "@RegistrationID",
                SqlDbType.Char,
                32)
                .Value =
                    registrationID;

            await failCommand
                .ExecuteNonQueryAsync(
                    cancellationToken);

            await transaction.CommitAsync(
                cancellationToken);

            return null;
        }

        const string consumeSql = """
            UPDATE dbo.PendingDonorRegistration
            SET ConsumedAtUtc = @ConsumedAtUtc
            WHERE RegistrationID =
                  @RegistrationID
              AND ConsumedAtUtc IS NULL;
            """;

        await using (var consumeCommand =
                     new SqlCommand(
                         consumeSql,
                         connection,
                         transaction))
        {
            consumeCommand.Parameters.Add(
                "@ConsumedAtUtc",
                SqlDbType.DateTime2)
                .Value =
                    nowUtc.UtcDateTime;

            consumeCommand.Parameters.Add(
                "@RegistrationID",
                SqlDbType.Char,
                32)
                .Value =
                    registrationID;

            var affectedRows =
                await consumeCommand
                    .ExecuteNonQueryAsync(
                        cancellationToken);

            if (affectedRows != 1)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return null;
            }
        }

        await transaction.CommitAsync(
            cancellationToken);

        var decryptedPayload =
            new byte[
                encryptedPayload.Length];

        try
        {
            using var aes =
                new AesGcm(
                    _encryptionKey,
                    authenticationTag.Length);

            aes.Decrypt(
                nonce,
                encryptedPayload,
                authenticationTag,
                decryptedPayload,
                Encoding.UTF8.GetBytes(
                    registrationID));

            var payload =
                JsonSerializer
                    .Deserialize<
                        RegistrationPayload>(
                        decryptedPayload);

            if (payload is null ||
                payload.EMAOrganizationID !=
                    EMAOrganizationID)
            {
                return null;
            }

            return new PendingDonorRegistrationRecord
            {
                RegistrationID =
                    registrationID,

                EMAOrganizationID =
                    payload.EMAOrganizationID,

                OfficialDonorName =
                    payload.OfficialDonorName,

                InteracDonorName =
                    payload.InteracDonorName,

                Address =
                    payload.Address,

                City =
                    payload.City,

                ProvinceStateCode =
                    payload.ProvinceStateCode,

                PostalCode =
                    payload.PostalCode,

                Country =
                    payload.Country,

                PhoneNumber =
                    payload.PhoneNumber,

                EmailAddress =
                    payload.EmailAddress,

                ExpiresAtUtc =
                    new DateTimeOffset(
                        expiresAtUtc,
                        TimeSpan.Zero)
            };
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        finally
        {
            CryptographicOperations
                .ZeroMemory(
                    decryptedPayload);
        }
    }

    public async Task RevokeAsync(
        string registrationID,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidRegistrationID(
                registrationID))
        {
            return;
        }

        const string sql = """
            UPDATE dbo.PendingDonorRegistration
            SET ConsumedAtUtc =
                COALESCE(
                    ConsumedAtUtc,
                    SYSUTCDATETIME())
            WHERE RegistrationID =
                  @RegistrationID;
            """;

        await using var connection =
            new SqlConnection(
                _connectionString);

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters.Add(
            "@RegistrationID",
            SqlDbType.Char,
            32)
            .Value =
                registrationID.Trim()
                    .ToLowerInvariant();

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private byte[] HashCode(
        string registrationID,
        string code)
    {
        using var hmac =
            new HMACSHA256(
                _codeKey);

        return hmac.ComputeHash(
            Encoding.UTF8.GetBytes(
                $"{registrationID}\n{code}"));
    }

    private byte[] HashDestination(
        string emailAddress)
    {
        using var hmac =
            new HMACSHA256(
                _destinationKey);

        return hmac.ComputeHash(
            Encoding.UTF8.GetBytes(
                EmailAddressNormalizer
                    .Normalize(
                        emailAddress)));
    }

    private static byte[] DeriveKey(
        byte[] masterKey,
        string purpose)
    {
        using var hmac =
            new HMACSHA256(
                masterKey);

        return hmac.ComputeHash(
            Encoding.UTF8.GetBytes(
                purpose));
    }

    private static bool IsValidRegistrationID(
        string? registrationID)
    {
        return registrationID is
                   { Length: 32 } &&
               registrationID.All(
                   character =>
                       character is >= '0'
                           and <= '9' or
                           >= 'a' and <= 'f' or
                           >= 'A' and <= 'F');
    }

    private sealed record RegistrationPayload(
        int EMAOrganizationID,
        string OfficialDonorName,
        string? InteracDonorName,
        string Address,
        string City,
        string ProvinceStateCode,
        string PostalCode,
        string Country,
        string PhoneNumber,
        string EmailAddress);
}
