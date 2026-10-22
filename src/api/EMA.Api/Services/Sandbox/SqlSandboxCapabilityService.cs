using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace EMA.Api.Services.Sandbox;

public sealed class SqlSandboxCapabilityService :
    ISandboxCapabilityService
{
    private const string TokenPrefix =
        "sc1";

    private readonly string _connectionString;
    private readonly byte[] _secret;

    public SqlSandboxCapabilityService(
        IConfiguration configuration)
    {
        _connectionString =
            configuration["SqlConnectionString"]
            ?? Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString.");

        var encodedSecret =
            configuration[
                "SANDBOX_CAPABILITY_SECRET"]
            ?? Environment.GetEnvironmentVariable(
                "SANDBOX_CAPABILITY_SECRET");

        try
        {
            _secret =
                Convert.FromBase64String(
                    encodedSecret ?? string.Empty);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "SANDBOX_CAPABILITY_SECRET must be a base64-encoded secret.",
                exception);
        }

        if (_secret.Length < 32)
        {
            throw new InvalidOperationException(
                "SANDBOX_CAPABILITY_SECRET must decode to at least 32 bytes.");
        }
    }

    public async Task<string> CreateAsync(
        int sandboxOrganizationID,
        CancellationToken cancellationToken = default)
    {
        if (sandboxOrganizationID <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sandboxOrganizationID));
        }

        var randomBytes =
            RandomNumberGenerator.GetBytes(
                32);

        var capability =
            string.Create(
                CultureInfo.InvariantCulture,
                $"{TokenPrefix}.{sandboxOrganizationID}.{Base64UrlEncode(randomBytes)}");

        var capabilityHash =
            ComputeHash(
                capability);

        const string sql = """
            IF EXISTS
            (
                SELECT 1
                FROM dbo.SandboxCapability WITH
                    (UPDLOCK, HOLDLOCK)
                WHERE SandboxOrganizationID =
                      @SandboxOrganizationID
                  AND RevokedAtUtc IS NULL
            )
            BEGIN
                THROW 51070,
                    'An active sandbox capability already exists.',
                    1;
            END;

            DELETE FROM dbo.SandboxCapability
            WHERE SandboxOrganizationID =
                  @SandboxOrganizationID
              AND RevokedAtUtc IS NOT NULL;

            INSERT INTO dbo.SandboxCapability
            (
                SandboxOrganizationID,
                CapabilityHash,
                CreatedAtUtc,
                RevokedAtUtc
            )
            VALUES
            (
                @SandboxOrganizationID,
                @CapabilityHash,
                SYSUTCDATETIME(),
                NULL
            );
            """;

        await using var connection =
            new SqlConnection(
                _connectionString);

        await connection.OpenAsync(
            cancellationToken);

        await using var transaction =
            (SqlTransaction)
            await connection.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        await using var command =
            new SqlCommand(
                sql,
                connection,
                transaction);

        command.Parameters.Add(
            "@SandboxOrganizationID",
            SqlDbType.Int)
            .Value =
                sandboxOrganizationID;

        command.Parameters.Add(
            "@CapabilityHash",
            SqlDbType.Binary,
            32)
            .Value =
                capabilityHash;

        await command.ExecuteNonQueryAsync(
            cancellationToken);

        await transaction.CommitAsync(
            cancellationToken);

        return capability;
    }

    public async Task<int?> ResolveOrganizationIDAsync(
        string? capability,
        CancellationToken cancellationToken = default)
    {
        if (!TryParse(
                capability,
                out var organizationID))
        {
            return null;
        }

        const string sql = """
            SELECT CapabilityHash
            FROM dbo.SandboxCapability
            WHERE SandboxOrganizationID =
                  @SandboxOrganizationID
              AND RevokedAtUtc IS NULL;
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
            "@SandboxOrganizationID",
            SqlDbType.Int)
            .Value =
                organizationID;

        var storedHash =
            await command.ExecuteScalarAsync(
                cancellationToken) as byte[];

        if (storedHash is null ||
            storedHash.Length != 32)
        {
            return null;
        }

        var suppliedHash =
            ComputeHash(
                capability!);

        return CryptographicOperations
            .FixedTimeEquals(
                storedHash,
                suppliedHash)
            ? organizationID
            : null;
    }

    private byte[] ComputeHash(
        string capability)
    {
        using var hmac =
            new HMACSHA256(
                _secret);

        return hmac.ComputeHash(
            Encoding.UTF8.GetBytes(
                capability));
    }

    private static bool TryParse(
        string? capability,
        out int organizationID)
    {
        organizationID = 0;

        if (string.IsNullOrWhiteSpace(
                capability) ||
            capability.Length > 256)
        {
            return false;
        }

        var parts =
            capability.Split(
                '.',
                StringSplitOptions.None);

        return parts.Length == 3 &&
               parts[0].Equals(
                   TokenPrefix,
                   StringComparison.Ordinal) &&
               int.TryParse(
                   parts[1],
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out organizationID) &&
               organizationID > 0 &&
               TryBase64UrlDecode(
                   parts[2],
                   out var randomBytes) &&
               randomBytes.Length == 32;
    }

    private static string Base64UrlEncode(
        byte[] value)
    {
        return Convert
            .ToBase64String(
                value)
            .TrimEnd('=')
            .Replace(
                '+',
                '-')
            .Replace(
                '/',
                '_');
    }

    private static bool TryBase64UrlDecode(
        string value,
        out byte[] decoded)
    {
        decoded =
            Array.Empty<byte>();

        if (string.IsNullOrWhiteSpace(
                value) ||
            value.Any(
                character =>
                    !char.IsAsciiLetterOrDigit(
                        character) &&
                    character is not '-' and not '_'))
        {
            return false;
        }

        var padded =
            value
                .Replace(
                    '-',
                    '+')
                .Replace(
                    '_',
                    '/');

        padded +=
            new string(
                '=',
                (4 - padded.Length % 4) % 4);

        try
        {
            decoded =
                Convert.FromBase64String(
                    padded);

            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
