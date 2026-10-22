using System.Net.Mail;

namespace EMA.Api.Services.Authentication;

public static class EmailAddressNormalizer
{
    public static string Normalize(string emailAddress)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(emailAddress);

        var trimmed = emailAddress.Trim();

        if (trimmed.Length > 320)
        {
            throw new ArgumentException(
                "The email address is too long.",
                nameof(emailAddress));
        }

        MailAddress parsed;

        try
        {
            parsed = new MailAddress(trimmed);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException(
                "Please enter a valid email address.",
                nameof(emailAddress),
                exception);
        }

        if (!string.Equals(
                parsed.Address,
                trimmed,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Please enter a valid email address.",
                nameof(emailAddress));
        }

        return parsed.Address.ToLowerInvariant();
    }
}