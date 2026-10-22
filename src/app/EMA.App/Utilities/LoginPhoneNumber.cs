namespace EMA.App.Utilities;

/// <summary>
/// Provides one consistent North American phone-number rule for every login page.
/// </summary>
public static class LoginPhoneNumber
{
    public static string NormalizeDigits(
        string? value)
    {
        var digits =
            new string(
                (value ?? string.Empty)
                    .Where(char.IsDigit)
                    .ToArray());

        if (digits.Length == 11 &&
            digits.StartsWith(
                "1",
                StringComparison.Ordinal))
        {
            digits =
                digits[1..];
        }

        return digits;
    }

    public static string Format(
        string? value)
    {
        var digits =
            NormalizeDigits(
                value);

        if (digits.Length != 10)
        {
            return value?.Trim() ??
                   string.Empty;
        }

        return $"{digits[..3]}-{digits.Substring(3, 3)}-{digits.Substring(6, 4)}";
    }
}
