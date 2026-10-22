using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace EMA.Api.Services.Security;

public sealed class
    ProductionSecurityConfigurationValidator :
        IHostedService
{
    private static readonly string[]
        RequiredBase64Secrets =
        [
            "API_ACCESS_TOKEN_SIGNING_KEY",
            "API_RATE_LIMIT_SECRET",
            "DONOR_ACCESS_TOKEN_SIGNING_KEY",
            "PENDING_REGISTRATION_SECRET",
            "SANDBOX_CAPABILITY_SECRET"
        ];

    private static readonly string[]
        RequiredValues =
        [
            "SqlConnectionString",
            "API_ACCESS_TOKEN_ISSUER",
            "API_ACCESS_TOKEN_AUDIENCE",
            "API_ALLOWED_ORIGINS",
            "DONOR_PORTAL_BASE_URL",
            "TWO_FACTOR_CODE_SECRET",
            "COMMUNICATION_SERVICES_CONNECTION_STRING",
            "ACS_EMAIL_SENDER",
            "EMA_SUPPORT_EMAIL",
            "BlobStorageConnectionString",
            "BlobContainerName",
            "GraphTenantId",
            "GraphClientId",
            "GraphClientSecret",
            "EtransferMailboxAddress",
            "EtransferIngestSchedule",
            "EtransferAliasProvisioningMode",
            "SandboxEmailDomain"
        ];

    private static readonly string[]
        RequiredPositiveIntegers =
        [
            "PLATFORM_ORGANIZATION_ID",
            "PLATFORM_ADMIN_CATEGORY_ID",
            "TWO_FACTOR_GLOBAL_MAX_CODES_PER_15_MINUTES",
            "AUTOMATED_SERVICE_USER_ID",
            "SandboxTemplateOrganizationID",
            "SandboxTemplateUserID"
        ];

    private static readonly HashSet<string>
        LocalExampleSecrets =
        new(
            StringComparer.Ordinal)
        {
            "JFxb2IIRjSB0OGtQz1BEatrEZ+1ddLoglmtd/HKBM7Q=",
            "Cp0kX202jowfqBb1vczWt4NZ4LhvD4zj6vT4jOCaA0I=",
            "C3uUvq4Ufb6kGRrJT6V0fIFoTQcRvgGNdCxJaGbdr7w=",
            "yyR7MFHhj1HpYeASCCvY1FtW7ya3Md9cu+GVr/n+BOU=",
            "GisJhaCdlwhllTUBPpvbfZMkDRY6NPvN48PyfZF+5sA="
        };

    private readonly IConfiguration
        _configuration;

    public ProductionSecurityConfigurationValidator(
        IConfiguration configuration)
    {
        _configuration =
            configuration;
    }

    public Task StartAsync(
        CancellationToken cancellationToken)
    {
        if (ApiRuntimeEnvironment
                .IsDevelopment)
        {
            return Task.CompletedTask;
        }

        foreach (var settingName in
                 RequiredValues)
        {
            RequireValue(
                settingName);
        }

        var decodedSecrets =
            new Dictionary<
                string,
                byte[]>(
                StringComparer.Ordinal);

        foreach (var settingName in
                 RequiredBase64Secrets)
        {
            decodedSecrets[settingName] =
                RequireBase64Secret(
                    settingName);
        }

        EnsureSecretsAreDistinct(
            decodedSecrets);

        foreach (var settingName in
                 RequiredPositiveIntegers)
        {
            RequirePositiveInteger(
                settingName);
        }

        RequirePositiveIntegerList(
            "ORGANIZATION_ADMIN_ROLE_IDS");

        RequireHttpsUrl(
            "API_ACCESS_TOKEN_ISSUER",
            requireOriginOnly: false);

        RequireHttpsUrl(
            "DONOR_PORTAL_BASE_URL",
            requireOriginOnly: false);

        RequireAllowedOrigins();

        var twoFactorSecret =
            RequireValue(
                "TWO_FACTOR_CODE_SECRET");

        if (twoFactorSecret.Length < 32 ||
            string.Equals(
                twoFactorSecret,
                "ema-local-two-factor-secret-change-me",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "TWO_FACTOR_CODE_SECRET must contain at least 32 non-example characters.");
        }

        RequireEmailAddress(
            "ACS_EMAIL_SENDER");

        RequireEmailAddress(
            "EMA_SUPPORT_EMAIL");

        RequireEmailAddress(
            "EtransferMailboxAddress");

        RequireDomainName(
            "SandboxEmailDomain");

        ValidateAliasProvisioning();

        ValidateLifetimeSettings();

        RequireBoundedInteger(
            "SandboxMaxTemplateDonations",
            minimumValue: 1,
            maximumValue: 10_000);

        RequireBoundedInteger(
            "SandboxActiveDays",
            minimumValue: 1,
            maximumValue: 365);

        return Task.CompletedTask;
    }

    public Task StopAsync(
        CancellationToken cancellationToken) =>
        Task.CompletedTask;

    private string RequireValue(
        string settingName)
    {
        var value =
            _configuration[settingName]
                ?.Trim();

        if (string.IsNullOrWhiteSpace(
                value))
        {
            throw new InvalidOperationException(
                $"Missing required production setting '{settingName}'.");
        }

        if (value.Contains(
                "REPLACE_WITH",
                StringComparison.OrdinalIgnoreCase) ||
            value.Contains(
                "example.invalid",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                value,
                "UseDevelopmentStorage=true",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                value,
                "ema-blobs-local",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{settingName} contains a local-development placeholder.");
        }

        return value;
    }

    private byte[] RequireBase64Secret(
        string settingName)
    {
        var encodedValue =
            RequireValue(
                settingName);

        if (LocalExampleSecrets.Contains(
                encodedValue))
        {
            throw new InvalidOperationException(
                $"{settingName} must not use the committed local-development example.");
        }

        byte[] decodedValue;

        try
        {
            decodedValue =
                Convert.FromBase64String(
                    encodedValue);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                $"{settingName} must be Base64 encoded.",
                exception);
        }

        if (decodedValue.Length < 32)
        {
            throw new InvalidOperationException(
                $"{settingName} must contain at least 32 random bytes.");
        }

        return decodedValue;
    }

    private void RequirePositiveInteger(
        string settingName)
    {
        if (!int.TryParse(
                RequireValue(
                    settingName),
                out var value) ||
            value <= 0)
        {
            throw new InvalidOperationException(
                $"{settingName} must be a positive integer.");
        }
    }

    private void RequirePositiveIntegerList(
        string settingName)
    {
        var values =
            RequireValue(
                    settingName)
                .Split(
                    ',',
                    StringSplitOptions
                        .RemoveEmptyEntries |
                    StringSplitOptions
                        .TrimEntries);

        if (values.Length == 0 ||
            values.Any(
                value =>
                    !int.TryParse(
                        value,
                        out var parsed) ||
                    parsed <= 0))
        {
            throw new InvalidOperationException(
                $"{settingName} must contain one or more comma-separated positive integers.");
        }
    }

    private void RequireHttpsUrl(
        string settingName,
        bool requireOriginOnly)
    {
        var rawValue =
            RequireValue(
                settingName);

        if (!Uri.TryCreate(
                rawValue,
                UriKind.Absolute,
                out var uri) ||
            !string.Equals(
                uri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(
                uri.Host) ||
            !string.IsNullOrEmpty(
                uri.UserInfo) ||
            !string.IsNullOrEmpty(
                uri.Query) ||
            !string.IsNullOrEmpty(
                uri.Fragment) ||
            requireOriginOnly &&
            uri.AbsolutePath != "/")
        {
            throw new InvalidOperationException(
                $"{settingName} must be a valid HTTPS URL"
                + (
                    requireOriginOnly
                        ? " containing only an origin."
                        : "."
                ));
        }
    }

    private void RequireAllowedOrigins()
    {
        var origins =
            RequireValue(
                    "API_ALLOWED_ORIGINS")
                .Split(
                    ',',
                    StringSplitOptions
                        .RemoveEmptyEntries |
                    StringSplitOptions
                        .TrimEntries);

        if (origins.Length == 0)
        {
            throw new InvalidOperationException(
                "API_ALLOWED_ORIGINS must list at least one frontend origin.");
        }

        foreach (var origin in origins)
        {
            if (!Uri.TryCreate(
                    origin,
                    UriKind.Absolute,
                    out var uri) ||
                !string.Equals(
                    uri.Scheme,
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(
                    uri.Host) ||
                !string.IsNullOrEmpty(
                    uri.UserInfo) ||
                !string.IsNullOrEmpty(
                    uri.Query) ||
                !string.IsNullOrEmpty(
                    uri.Fragment) ||
                uri.AbsolutePath != "/")
            {
                throw new InvalidOperationException(
                    "API_ALLOWED_ORIGINS must contain only comma-separated HTTPS origins.");
            }
        }
    }

    private static void EnsureSecretsAreDistinct(
        IReadOnlyDictionary<
            string,
            byte[]> secrets)
    {
        var entries =
            secrets.ToArray();

        for (var firstIndex = 0;
             firstIndex < entries.Length;
             firstIndex++)
        {
            for (var secondIndex =
                     firstIndex + 1;
                 secondIndex <
                 entries.Length;
                 secondIndex++)
            {
                if (!entries[firstIndex]
                        .Value
                        .AsSpan()
                        .SequenceEqual(
                            entries[secondIndex]
                                .Value))
                {
                    continue;
                }

                throw new InvalidOperationException(
                    $"{entries[firstIndex].Key} and {entries[secondIndex].Key} must use different secrets.");
            }
        }
    }

    private void ValidateLifetimeSettings()
    {
        var maximumMinutes =
            ReadOptionalBoundedInteger(
                "API_ACCESS_TOKEN_MAX_LIFETIME_MINUTES",
                defaultValue: 480,
                minimumValue: 5,
                maximumValue: 1440);

        var sessionMinutes =
            ReadOptionalBoundedInteger(
                "API_ACCESS_TOKEN_LIFETIME_MINUTES",
                defaultValue: 60,
                minimumValue: 5,
                maximumValue: 480);

        if (sessionMinutes >
            maximumMinutes)
        {
            throw new InvalidOperationException(
                "API_ACCESS_TOKEN_LIFETIME_MINUTES cannot exceed API_ACCESS_TOKEN_MAX_LIFETIME_MINUTES.");
        }

        ReadOptionalBoundedInteger(
            "DONOR_SESSION_TOKEN_LIFETIME_MINUTES",
            defaultValue: 720,
            minimumValue: 5,
            maximumValue: 1440);

        ReadOptionalBoundedInteger(
            "GIVING_HISTORY_TOKEN_LIFETIME_MINUTES",
            defaultValue: 10080,
            minimumValue: 5,
            maximumValue: 43200);
    }

    private void RequireEmailAddress(
        string settingName)
    {
        var value =
            RequireValue(
                settingName);

        if (!MailAddress.TryCreate(
                value,
                out var parsedAddress) ||
            !string.Equals(
                parsedAddress.Address,
                value,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{settingName} must contain one valid email address.");
        }
    }

    private void RequireDomainName(
        string settingName)
    {
        var value =
            RequireValue(
                    settingName)
                .Trim()
                .TrimStart('@');

        if (Uri.CheckHostName(
                value) !=
            UriHostNameType.Dns ||
            value.Contains(
                "..",
                StringComparison.Ordinal) ||
            value.Length > 253)
        {
            throw new InvalidOperationException(
                $"{settingName} must contain one DNS domain name.");
        }
    }

    private void ValidateAliasProvisioning()
    {
        var mode =
            RequireValue(
                "EtransferAliasProvisioningMode");

        if (string.Equals(
                mode,
                "DatabaseOnly",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!string.Equals(
                mode,
                "ExchangeOnline",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "EtransferAliasProvisioningMode must be DatabaseOnly or ExchangeOnline.");
        }

        RequireValue(
            "ExchangeOnlineOrganization");

        RequireValue(
            "ExchangeOnlineMailboxIdentity");

        RequireValue(
            "ExchangeOnlineAppID");

        var encodedCertificate =
            RequireValue(
                "ExchangeOnlineCertificateBase64");

        try
        {
            if (Convert.FromBase64String(
                    encodedCertificate)
                .Length == 0)
            {
                throw new FormatException();
            }
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "ExchangeOnlineCertificateBase64 must contain a Base64-encoded certificate.",
                exception);
        }
    }

    private void RequireBoundedInteger(
        string settingName,
        int minimumValue,
        int maximumValue)
    {
        var rawValue =
            RequireValue(
                settingName);

        if (!int.TryParse(
                rawValue,
                out var value) ||
            value < minimumValue ||
            value > maximumValue)
        {
            throw new InvalidOperationException(
                $"{settingName} must be between {minimumValue} and {maximumValue}.");
        }
    }

    private int ReadOptionalBoundedInteger(
        string settingName,
        int defaultValue,
        int minimumValue,
        int maximumValue)
    {
        var rawValue =
            _configuration[settingName];

        if (string.IsNullOrWhiteSpace(
                rawValue))
        {
            return defaultValue;
        }

        if (!int.TryParse(
                rawValue,
                out var value) ||
            value < minimumValue ||
            value > maximumValue)
        {
            throw new InvalidOperationException(
                $"{settingName} must be between {minimumValue} and {maximumValue}.");
        }

        return value;
    }
}
