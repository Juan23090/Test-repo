using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace EMA.Api.Services.Authentication;

public sealed class DonorAccessTokenService :
    IDonorAccessTokenService
{
    private const int TokenVersion = 1;

    private const string DonorSessionPurpose =
        "donor-session";

    private const string GivingHistoryPurpose =
        "giving-history";

    private const string DonationChangesPurpose =
        "donation-changes";

    private readonly byte[] _signingKey;
    private readonly string _connectionString;
    private readonly string _portalBaseUrl;
    private readonly TimeSpan _donorSessionLifetime;
    private readonly TimeSpan _givingHistoryLifetime;
    private readonly TimeSpan _donationChangesLifetime;

    public DonorAccessTokenService(
        IConfiguration configuration)
    {
        var configuredKey =
            configuration[
                "DONOR_ACCESS_TOKEN_SIGNING_KEY"];

        if (string.IsNullOrWhiteSpace(
                configuredKey))
        {
            throw new InvalidOperationException(
                "Missing DONOR_ACCESS_TOKEN_SIGNING_KEY.");
        }

        try
        {
            _signingKey =
                Convert.FromBase64String(
                    configuredKey);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "DONOR_ACCESS_TOKEN_SIGNING_KEY must be a Base64-encoded key.",
                exception);
        }

        if (_signingKey.Length < 32)
        {
            throw new InvalidOperationException(
                "DONOR_ACCESS_TOKEN_SIGNING_KEY must contain at least 32 bytes.");
        }

        _connectionString =
            configuration[
                "SqlConnectionString"]
            ?? Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString.");

        _portalBaseUrl =
            (
                configuration[
                    "DONOR_PORTAL_BASE_URL"]
                ?? "https://emailmoney.app"
            ).TrimEnd('/');

        _donorSessionLifetime =
            ReadLifetime(
                configuration,
                "DONOR_SESSION_TOKEN_LIFETIME_MINUTES",
                defaultMinutes: 720);

        _givingHistoryLifetime =
            ReadLifetime(
                configuration,
                "GIVING_HISTORY_TOKEN_LIFETIME_MINUTES",
                defaultMinutes: 10080);

        _donationChangesLifetime =
            ReadLifetime(
                configuration,
                "DONATION_CHANGES_TOKEN_LIFETIME_MINUTES",
                defaultMinutes: 10080);
    }

    public DonorAccessToken CreateDonorSessionToken(
        int donorID,
        int EMAOrganizationID)
    {
        return CreateToken(
            donorID,
            EMAOrganizationID,
            DonorSessionPurpose,
            _donorSessionLifetime);
    }

    public async Task<string>
        CreateGivingHistoryUrlAsync(
            int donorID,
            CancellationToken cancellationToken = default)
    {
        if (donorID <= 0)
        {
            return GetDonorLoginUrl();
        }

        var organizationID =
            await GetDonorOrganizationIDAsync(
                donorID,
                cancellationToken);

        if (!organizationID.HasValue)
        {
            return GetDonorLoginUrl();
        }

        var accessToken =
            CreateToken(
                donorID,
                organizationID.Value,
                GivingHistoryPurpose,
                _givingHistoryLifetime);

        return
            $"{_portalBaseUrl}/giving-history#token={Uri.EscapeDataString(accessToken.Token)}";
    }

    public string GetDonorLoginUrl()
    {
        return
            $"{_portalBaseUrl}/login";
    }

    public string CreateDonationChangesUrl(
        long donationID,
        int donorID,
        int EMAOrganizationID)
    {
        if (donationID <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(donationID));
        }

        var accessToken =
            CreateToken(
                donorID,
                EMAOrganizationID,
                DonationChangesPurpose,
                _donationChangesLifetime,
                donationID);

        return
            $"{_portalBaseUrl}/donor-directed-changes/{donationID}#token={Uri.EscapeDataString(accessToken.Token)}";
    }

    public bool TryValidateDonorSessionToken(
        string token,
        out DonorAccessIdentity? identity)
    {
        return TryValidateToken(
            token,
            DonorSessionPurpose,
            out identity);
    }

    public bool TryValidateGivingHistoryToken(
        string token,
        out DonorAccessIdentity? identity)
    {
        return TryValidateToken(
            token,
            GivingHistoryPurpose,
            out identity);
    }

    public bool TryValidateDonationChangesToken(
        string token,
        long donationID,
        out DonorAccessIdentity? identity)
    {
        if (donationID <= 0)
        {
            identity = null;
            return false;
        }

        return TryValidateToken(
            token,
            DonationChangesPurpose,
            out identity,
            donationID);
    }

    private DonorAccessToken CreateToken(
        int donorID,
        int EMAOrganizationID,
        string purpose,
        TimeSpan lifetime,
        long donationID = 0)
    {
        if (donorID <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(donorID));
        }

        if (EMAOrganizationID <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(EMAOrganizationID));
        }

        if (donationID < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(donationID));
        }

        var issuedAtUtc =
            DateTimeOffset.UtcNow;

        var expiresAtUtc =
            issuedAtUtc.Add(
                lifetime);

        var payload =
            new TokenPayload
            {
                Version =
                    TokenVersion,

                DonorID =
                    donorID,

                EMAOrganizationID =
                    EMAOrganizationID,

                Purpose =
                    purpose,

                DonationID =
                    donationID,

                IssuedAtUnixSeconds =
                    issuedAtUtc.ToUnixTimeSeconds(),

                ExpiresAtUnixSeconds =
                    expiresAtUtc.ToUnixTimeSeconds(),

                Nonce =
                    Base64UrlEncode(
                        RandomNumberGenerator.GetBytes(
                            16))
            };

        var payloadBytes =
            JsonSerializer.SerializeToUtf8Bytes(
                payload);

        var encodedPayload =
            Base64UrlEncode(
                payloadBytes);

        var signature =
            CalculateSignature(
                encodedPayload);

        var token =
            $"{encodedPayload}.{Base64UrlEncode(signature)}";

        return new DonorAccessToken(
            token,
            expiresAtUtc);
    }

    private bool TryValidateToken(
        string token,
        string expectedPurpose,
        out DonorAccessIdentity? identity,
        long? expectedDonationID = null)
    {
        identity = null;

        if (string.IsNullOrWhiteSpace(
                token) ||
            token.Length > 4096)
        {
            return false;
        }

        var parts =
            token.Split(
                '.',
                StringSplitOptions.None);

        if (parts.Length != 2 ||
            string.IsNullOrWhiteSpace(
                parts[0]) ||
            string.IsNullOrWhiteSpace(
                parts[1]))
        {
            return false;
        }

        byte[] suppliedSignature;

        try
        {
            suppliedSignature =
                Base64UrlDecode(
                    parts[1]);
        }
        catch (FormatException)
        {
            return false;
        }

        var expectedSignature =
            CalculateSignature(
                parts[0]);

        if (suppliedSignature.Length !=
            expectedSignature.Length ||
            !CryptographicOperations.FixedTimeEquals(
                suppliedSignature,
                expectedSignature))
        {
            return false;
        }

        TokenPayload? payload;

        try
        {
            var payloadBytes =
                Base64UrlDecode(
                    parts[0]);

            payload =
                JsonSerializer.Deserialize<TokenPayload>(
                    payloadBytes);
        }
        catch (
            Exception exception)
            when (
                exception is FormatException or
                JsonException)
        {
            return false;
        }

        if (payload is null ||
            payload.Version != TokenVersion ||
            payload.DonorID <= 0 ||
            payload.EMAOrganizationID <= 0 ||
            (expectedDonationID.HasValue &&
             payload.DonationID != expectedDonationID.Value) ||
            !string.Equals(
                payload.Purpose,
                expectedPurpose,
                StringComparison.Ordinal))
        {
            return false;
        }

        var nowUnixSeconds =
            DateTimeOffset.UtcNow
                .ToUnixTimeSeconds();

        if (payload.ExpiresAtUnixSeconds <=
            nowUnixSeconds)
        {
            return false;
        }

        if (payload.IssuedAtUnixSeconds >
            nowUnixSeconds + 300)
        {
            return false;
        }

        identity =
            new DonorAccessIdentity(
                payload.DonorID,
                payload.EMAOrganizationID);

        return true;
    }

    private byte[] CalculateSignature(
        string encodedPayload)
    {
        using var hmac =
            new HMACSHA256(
                _signingKey);

        return hmac.ComputeHash(
            Encoding.UTF8.GetBytes(
                encodedPayload));
    }

    private async Task<int?>
        GetDonorOrganizationIDAsync(
            int donorID,
            CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EMAOrganizationID
            FROM dbo.Donor
            WHERE EMADonorID = @DonorID;
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
            "@DonorID",
            SqlDbType.Int)
            .Value = donorID;

        var result =
            await command.ExecuteScalarAsync(
                cancellationToken);

        if (result is null ||
            result == DBNull.Value)
        {
            return null;
        }

        var organizationID =
            Convert.ToInt32(
                result);

        return organizationID > 0
            ? organizationID
            : null;
    }

    private static TimeSpan ReadLifetime(
        IConfiguration configuration,
        string settingName,
        int defaultMinutes)
    {
        var configuredValue =
            configuration[
                settingName];

        if (!int.TryParse(
                configuredValue,
                out var minutes) ||
            minutes <= 0 ||
            minutes > 525600)
        {
            minutes =
                defaultMinutes;
        }

        return TimeSpan.FromMinutes(
            minutes);
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

    private static byte[] Base64UrlDecode(
    string value)
    {
        var normalized =
            value
                .Replace(
                    '-',
                    '+')
                .Replace(
                    '_',
                    '/');

        normalized = (normalized.Length % 4)
            switch
        {
            0 => normalized,
            2 => normalized + "==",
            3 => normalized + "=",
            _ => throw new FormatException(
                "Invalid Base64 URL value.")
        };

        return Convert.FromBase64String(
            normalized);
    }

    private sealed class TokenPayload
    {
        [JsonPropertyName("v")]
        public int Version { get; set; }

        [JsonPropertyName("did")]
        public int DonorID { get; set; }

        [JsonPropertyName("oid")]
        public int EMAOrganizationID { get; set; }

        [JsonPropertyName("pur")]
        public string Purpose { get; set; } =
            string.Empty;

        [JsonPropertyName("donationId")]
        public long DonationID { get; set; }

        [JsonPropertyName("iat")]
        public long IssuedAtUnixSeconds { get; set; }

        [JsonPropertyName("exp")]
        public long ExpiresAtUnixSeconds { get; set; }

        [JsonPropertyName("nonce")]
        public string Nonce { get; set; } =
            string.Empty;
    }
}
