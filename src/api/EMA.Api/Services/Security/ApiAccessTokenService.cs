using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace EMA.Api.Services.Security;

public sealed class ApiAccessTokenService :
    IApiAccessTokenService
{
    private const int TokenVersion = 1;

    private const string TokenPrefix =
        "ema1";

    private const int MaxTokenLength =
        8192;

    private const int MaxClaimCount =
        100;

    private static readonly TimeSpan ClockSkew =
        TimeSpan.FromMinutes(2);

    private readonly byte[] _signingKey;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly TimeSpan _maximumTokenLifetime;

    public ApiAccessTokenService(
        IConfiguration configuration)
    {
        var configuredKey =
            configuration[
                "API_ACCESS_TOKEN_SIGNING_KEY"];

        if (string.IsNullOrWhiteSpace(
                configuredKey))
        {
            throw new InvalidOperationException(
                "Missing API_ACCESS_TOKEN_SIGNING_KEY.");
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
                "API_ACCESS_TOKEN_SIGNING_KEY must be a Base64-encoded key.",
                exception);
        }

        if (_signingKey.Length < 32)
        {
            throw new InvalidOperationException(
                "API_ACCESS_TOKEN_SIGNING_KEY must contain at least 32 bytes.");
        }

        _issuer =
            configuration[
                "API_ACCESS_TOKEN_ISSUER"]
            ?? "https://api.emailmoney.app";

        _audience =
            configuration[
                "API_ACCESS_TOKEN_AUDIENCE"]
            ?? "emailmoney.app";

        _maximumTokenLifetime =
            TimeSpan.FromMinutes(
                ReadPositiveInteger(
                    configuration,
                    "API_ACCESS_TOKEN_MAX_LIFETIME_MINUTES",
                    defaultValue: 480,
                    maximumValue: 1440));
    }

    public ApiAccessToken CreateToken(
        ApiRequestIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(
            identity);

        ValidateIdentityForIssuance(
            identity);

        var payload =
            new TokenPayload
            {
                Version =
                    TokenVersion,

                Issuer =
                    _issuer,

                Audience =
                    _audience,

                SessionID =
                    identity.SessionID.ToString("N"),

                SecurityStamp =
                    identity.SecurityStamp.ToString("N"),

                ActorType =
                    identity.ActorType.ToString(),

                EMAOrganizationID =
                    identity.EMAOrganizationID,

                UserID =
                    identity.UserID,

                DonorID =
                    identity.DonorID,

                RoleIDs =
                    identity.RoleIDs
                        .Distinct()
                        .OrderBy(value => value)
                        .ToArray(),

                AllowedCategoryIDs =
                    identity.AllowedCategoryIDs
                        .Distinct()
                        .OrderBy(value => value)
                        .ToArray(),

                IssuedAtUnixSeconds =
                    identity.IssuedAtUtc
                        .ToUnixTimeSeconds(),

                NotBeforeUnixSeconds =
                    identity.IssuedAtUtc
                        .ToUnixTimeSeconds(),

                ExpiresAtUnixSeconds =
                    identity.ExpiresAtUtc
                        .ToUnixTimeSeconds(),

                TokenID =
                    Base64UrlEncode(
                        RandomNumberGenerator
                            .GetBytes(16))
            };

        var payloadBytes =
            JsonSerializer.SerializeToUtf8Bytes(
                payload);

        var encodedPayload =
            Base64UrlEncode(
                payloadBytes);

        var signedValue =
            $"{TokenPrefix}.{encodedPayload}";

        var signature =
            CalculateSignature(
                signedValue);

        return new ApiAccessToken(
            $"{signedValue}.{Base64UrlEncode(signature)}",
            identity.ExpiresAtUtc);
    }

    public bool TryValidateToken(
        string token,
        out ApiRequestIdentity? identity)
    {
        identity = null;

        if (string.IsNullOrWhiteSpace(
                token) ||
            token.Length > MaxTokenLength)
        {
            return false;
        }

        var parts =
            token.Split(
                '.',
                StringSplitOptions.None);

        if (parts.Length != 3 ||
            !string.Equals(
                parts[0],
                TokenPrefix,
                StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(
                parts[1]) ||
            string.IsNullOrWhiteSpace(
                parts[2]))
        {
            return false;
        }

        byte[] suppliedSignature;

        try
        {
            suppliedSignature =
                Base64UrlDecode(
                    parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        var signedValue =
            $"{parts[0]}.{parts[1]}";

        var expectedSignature =
            CalculateSignature(
                signedValue);

        if (suppliedSignature.Length !=
            expectedSignature.Length ||
            !CryptographicOperations
                .FixedTimeEquals(
                    suppliedSignature,
                    expectedSignature))
        {
            return false;
        }

        TokenPayload? payload;

        try
        {
            payload =
                JsonSerializer.Deserialize<TokenPayload>(
                    Base64UrlDecode(
                        parts[1]));
        }
        catch (Exception exception)
            when (
                exception is FormatException or
                JsonException or
                ArgumentOutOfRangeException)
        {
            return false;
        }

        if (!TryCreateIdentity(
                payload,
                out identity))
        {
            identity = null;

            return false;
        }

        return true;
    }

    private bool TryCreateIdentity(
        TokenPayload? payload,
        out ApiRequestIdentity? identity)
    {
        identity = null;

        if (payload is null ||
            payload.Version != TokenVersion ||
            !string.Equals(
                payload.Issuer,
                _issuer,
                StringComparison.Ordinal) ||
            !string.Equals(
                payload.Audience,
                _audience,
                StringComparison.Ordinal) ||
            !Guid.TryParseExact(
                payload.SessionID,
                "N",
                out var sessionID) ||
            !Guid.TryParseExact(
                payload.SecurityStamp,
                "N",
                out var securityStamp) ||
            !Enum.TryParse<ApiActorType>(
                payload.ActorType,
                ignoreCase: false,
                out var actorType) ||
            !Enum.IsDefined(
                actorType) ||
            sessionID == Guid.Empty ||
            securityStamp == Guid.Empty ||
            payload.EMAOrganizationID <= 0 ||
            payload.UserID <= 0 ||
            string.IsNullOrWhiteSpace(
                payload.TokenID) ||
            payload.RoleIDs is null ||
            payload.AllowedCategoryIDs is null ||
            payload.RoleIDs.Length >
                MaxClaimCount ||
            payload.AllowedCategoryIDs.Length >
                MaxClaimCount ||
            payload.RoleIDs.Any(
                value => value <= 0) ||
            payload.AllowedCategoryIDs.Any(
                value => value <= 0) ||
            actorType == ApiActorType.Donor &&
            (!payload.DonorID.HasValue ||
             payload.DonorID.Value <= 0) ||
            actorType ==
                ApiActorType.OrganizationUser &&
            payload.DonorID.HasValue)
        {
            return false;
        }

        DateTimeOffset issuedAtUtc;
        DateTimeOffset notBeforeUtc;
        DateTimeOffset expiresAtUtc;

        try
        {
            issuedAtUtc =
                DateTimeOffset
                    .FromUnixTimeSeconds(
                        payload.IssuedAtUnixSeconds);

            notBeforeUtc =
                DateTimeOffset
                    .FromUnixTimeSeconds(
                        payload.NotBeforeUnixSeconds);

            expiresAtUtc =
                DateTimeOffset
                    .FromUnixTimeSeconds(
                        payload.ExpiresAtUnixSeconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        var nowUtc =
            DateTimeOffset.UtcNow;

        if (issuedAtUtc >
                nowUtc.Add(ClockSkew) ||
            notBeforeUtc >
                nowUtc.Add(ClockSkew) ||
            expiresAtUtc <=
                nowUtc ||
            expiresAtUtc <=
                issuedAtUtc ||
            expiresAtUtc - issuedAtUtc >
                _maximumTokenLifetime)
        {
            return false;
        }

        identity =
            new ApiRequestIdentity(
                sessionID,
                securityStamp,
                actorType,
                payload.EMAOrganizationID,
                payload.UserID,
                payload.DonorID,
                payload.RoleIDs
                    .Distinct()
                    .OrderBy(value => value)
                    .ToArray(),
                payload.AllowedCategoryIDs
                    .Distinct()
                    .OrderBy(value => value)
                    .ToArray(),
                issuedAtUtc,
                expiresAtUtc);

        return true;
    }

    private void ValidateIdentityForIssuance(
        ApiRequestIdentity identity)
    {
        if (identity.SessionID == Guid.Empty ||
            identity.SecurityStamp == Guid.Empty ||
            !Enum.IsDefined(
                identity.ActorType) ||
            identity.EMAOrganizationID <= 0 ||
            identity.UserID <= 0 ||
            identity.ExpiresAtUtc <=
                identity.IssuedAtUtc ||
            identity.ExpiresAtUtc -
                identity.IssuedAtUtc >
                _maximumTokenLifetime ||
            identity.RoleIDs.Count >
                MaxClaimCount ||
            identity.AllowedCategoryIDs.Count >
                MaxClaimCount ||
            identity.RoleIDs.Any(
                value => value <= 0) ||
            identity.AllowedCategoryIDs.Any(
                value => value <= 0) ||
            identity.ActorType ==
                ApiActorType.Donor &&
            (!identity.DonorID.HasValue ||
             identity.DonorID.Value <= 0) ||
            identity.ActorType ==
                ApiActorType.OrganizationUser &&
            identity.DonorID.HasValue)
        {
            throw new ArgumentException(
                "The access-token identity is invalid.",
                nameof(identity));
        }
    }

    private byte[] CalculateSignature(
        string signedValue)
    {
        using var hmac =
            new HMACSHA256(
                _signingKey);

        return hmac.ComputeHash(
            Encoding.UTF8.GetBytes(
                signedValue));
    }

    private static int ReadPositiveInteger(
        IConfiguration configuration,
        string settingName,
        int defaultValue,
        int maximumValue)
    {
        return int.TryParse(
                   configuration[settingName],
                   out var configuredValue) &&
               configuredValue > 0 &&
               configuredValue <= maximumValue
            ? configuredValue
            : defaultValue;
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

        normalized =
            (normalized.Length % 4)
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

        [JsonPropertyName("iss")]
        public string Issuer { get; set; } =
            string.Empty;

        [JsonPropertyName("aud")]
        public string Audience { get; set; } =
            string.Empty;

        [JsonPropertyName("sid")]
        public string SessionID { get; set; } =
            string.Empty;

        [JsonPropertyName("stamp")]
        public string SecurityStamp { get; set; } =
            string.Empty;

        [JsonPropertyName("act")]
        public string ActorType { get; set; } =
            string.Empty;

        [JsonPropertyName("oid")]
        public int EMAOrganizationID { get; set; }

        [JsonPropertyName("uid")]
        public int UserID { get; set; }

        [JsonPropertyName("did")]
        public int? DonorID { get; set; }

        [JsonPropertyName("roles")]
        public int[] RoleIDs { get; set; } =
            Array.Empty<int>();

        [JsonPropertyName("categories")]
        public int[] AllowedCategoryIDs { get; set; } =
            Array.Empty<int>();

        [JsonPropertyName("iat")]
        public long IssuedAtUnixSeconds { get; set; }

        [JsonPropertyName("nbf")]
        public long NotBeforeUnixSeconds { get; set; }

        [JsonPropertyName("exp")]
        public long ExpiresAtUnixSeconds { get; set; }

        [JsonPropertyName("jti")]
        public string TokenID { get; set; } =
            string.Empty;
    }
}