using System.Net.Mail;

namespace EMA.Api.Services.Authentication;

public enum LoginIdentifierKind
{
    PhoneNumber = 1,
    EmailAddress = 2
}

public sealed class NormalizedLoginIdentifier
{
    public LoginIdentifierKind Kind { get; init; }

    public string Value { get; init; } = string.Empty;

    public string DisplayValue { get; init; } = string.Empty;
}

public static class LoginIdentifierNormalizer
{
    public static bool TryNormalize(
        string? identifier,
        out NormalizedLoginIdentifier normalizedIdentifier,
        out string errorMessage)
    {
        normalizedIdentifier =
            new NormalizedLoginIdentifier();

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
            try
            {
                var normalizedEmail =
                    EmailAddressNormalizer
                        .Normalize(
                            trimmed);

                normalizedIdentifier =
                    new NormalizedLoginIdentifier
                    {
                        Kind =
                            LoginIdentifierKind
                                .EmailAddress,

                        Value =
                            normalizedEmail,

                        DisplayValue =
                            normalizedEmail
                    };

                return true;
            }
            catch (ArgumentException)
            {
                errorMessage =
                    "Please enter a valid email address.";

                return false;
            }
        }

        var digits =
            new string(
                trimmed
                    .Where(char.IsDigit)
                    .ToArray());

        if (digits.Length == 11 &&
            digits.StartsWith('1'))
        {
            digits =
                digits[1..];
        }

        if (digits.Length != 10)
        {
            errorMessage =
                "Please enter a valid 10 digit phone number or email address.";

            return false;
        }

        normalizedIdentifier =
            new NormalizedLoginIdentifier
            {
                Kind =
                    LoginIdentifierKind
                        .PhoneNumber,

                Value =
                    digits,

                DisplayValue =
                    $"{digits[..3]}-{digits.Substring(3, 3)}-{digits.Substring(6, 4)}"
            };

        return true;
    }
}
