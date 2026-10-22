using System.Net;
using System.Text.RegularExpressions;
using EMA.Api.Services.Authentication;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Services.Email;

public sealed class EmailTemplateService :
    IEmailTemplateService
{
    private static readonly Regex TokenRegex =
        new(
            @"\{\{\s*(?<key>[A-Za-z0-9_]+)\s*\}\}",
            RegexOptions.Compiled);

    private readonly IDonorAccessTokenService
        _donorAccessTokenService;

    private readonly ILogger<EmailTemplateService>
        _logger;

    public EmailTemplateService(
        IDonorAccessTokenService donorAccessTokenService,
        ILogger<EmailTemplateService> logger)
    {
        _donorAccessTokenService =
            donorAccessTokenService;

        _logger =
            logger;
    }

    public async Task<string> RenderTemplateAsync(
        string templateFileName,
        IReadOnlyDictionary<string, string?> values,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            templateFileName);

        var safeFileName =
            Path.GetFileName(
                templateFileName);

        var templatePath =
            Path.Combine(
                AppContext.BaseDirectory,
                "EmailTemplates",
                safeFileName);

        if (!File.Exists(
                templatePath))
        {
            throw new FileNotFoundException(
                $"Email template was not found: {templatePath}",
                templatePath);
        }

        var template =
            await File.ReadAllTextAsync(
                templatePath,
                cancellationToken);

        var resolvedValues =
            values.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);

        await ResolveDonorPortalUrlsAsync(
            template,
            resolvedValues,
            cancellationToken);

        return RenderString(
            template,
            resolvedValues);
    }

    public string RenderString(
        string template,
        IReadOnlyDictionary<string, string?> values)
    {
        ArgumentNullException.ThrowIfNull(
            template);

        ArgumentNullException.ThrowIfNull(
            values);

        return TokenRegex.Replace(
            template,
            match =>
            {
                var key =
                    match.Groups[
                        "key"].Value;

                if (!values.TryGetValue(
                        key,
                        out var value))
                {
                    throw new InvalidOperationException(
                        $"Missing email template value for placeholder: {{{{{key}}}}}");
                }

                return WebUtility.HtmlEncode(
                    value ?? string.Empty);
            });
    }

    private async Task ResolveDonorPortalUrlsAsync(
        string template,
        Dictionary<string, string?> values,
        CancellationToken cancellationToken)
    {
        if (template.Contains(
                "MakeChangesUrl",
                StringComparison.Ordinal))
        {
            values["MakeChangesUrl"] =
                _donorAccessTokenService
                    .GetDonorLoginUrl();

            if (IsDonorRecipient(
                    values) &&
                TryGetPositiveInt64(
                    values,
                    "EMADonationID",
                    out var donationID) &&
                TryGetPositiveInt32(
                    values,
                    "EMADonorID",
                    out var donationDonorID) &&
                TryGetPositiveInt32(
                    values,
                    "EMAOrganizationID",
                    out var organizationID))
            {
                values["MakeChangesUrl"] =
                    _donorAccessTokenService
                        .CreateDonationChangesUrl(
                            donationID,
                            donationDonorID,
                            organizationID);
            }
        }

        if (!template.Contains(
                "GivingHistoryUrl",
                StringComparison.Ordinal))
        {
            return;
        }

        values["GivingHistoryUrl"] =
            _donorAccessTokenService
                .GetDonorLoginUrl();

        if (!IsDonorRecipient(
                values))
        {
            return;
        }

        if (!values.TryGetValue(
                "EMADonorID",
                out var donorIDValue) ||
            !int.TryParse(
                donorIDValue,
                out var donorID) ||
            donorID <= 0)
        {
            return;
        }

        try
        {
            values["GivingHistoryUrl"] =
                await _donorAccessTokenService
                    .CreateGivingHistoryUrlAsync(
                        donorID,
                        cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Unable to create the giving-history link for donor {DonorID}.",
                donorID);
        }
    }

    private static bool IsDonorRecipient(
        IReadOnlyDictionary<string, string?> values)
    {
        return values.TryGetValue(
                   "RecipientType",
                   out var recipientType) &&
               string.Equals(
                   recipientType,
                   "Donor",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetPositiveInt32(
        IReadOnlyDictionary<string, string?> values,
        string key,
        out int result)
    {
        result = 0;

        return values.TryGetValue(
                   key,
                   out var value) &&
               int.TryParse(
                   value,
                   out result) &&
               result > 0;
    }

    private static bool TryGetPositiveInt64(
        IReadOnlyDictionary<string, string?> values,
        string key,
        out long result)
    {
        result = 0;

        return values.TryGetValue(
                   key,
                   out var value) &&
               long.TryParse(
                   value,
                   out result) &&
               result > 0;
    }
}
