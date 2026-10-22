using System.Net.Mail;

namespace EMA.App.Models;

public static class LoginIdentifierFormatter
{
    public static bool TryNormalize(
        string? identifier,
        out string normalizedIdentifier,
        out bool isEmailAddress,
        out string errorMessage)
    {
        normalizedIdentifier =
            string.Empty;

        isEmailAddress =
            false;

        errorMessage =
            string.Empty;

        var trimmed =
            identifier?.Trim()
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(
                trimmed))
        {
            errorMessage =
                "Please enter your phone number or email address.";

            return false;
        }

        if (trimmed.Contains('@'))
        {
            if (!MailAddress.TryCreate(
                    trimmed,
                    out var parsedAddress) ||
                !string.Equals(
                    parsedAddress.Address,
                    trimmed,
                    StringComparison.OrdinalIgnoreCase))
            {
                errorMessage =
                    "Please enter a valid email address.";

                return false;
            }

            normalizedIdentifier =
                parsedAddress.Address
                    .ToLowerInvariant();

            isEmailAddress =
                true;

            return true;
        }

        var phoneDigits =
            new string(
                trimmed
                    .Where(char.IsDigit)
                    .ToArray());

        if (phoneDigits.Length == 11 &&
            phoneDigits.StartsWith('1'))
        {
            phoneDigits =
                phoneDigits[1..];
        }

        if (phoneDigits.Length != 10)
        {
            errorMessage =
                "Please enter a valid 10 digit phone number or email address.";

            return false;
        }

        normalizedIdentifier =
            $"{phoneDigits[..3]}-{phoneDigits.Substring(3, 3)}-{phoneDigits.Substring(6, 4)}";

        return true;
    }
}
