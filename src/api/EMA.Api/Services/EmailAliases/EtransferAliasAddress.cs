namespace EMA_Api.Services.EmailAliases;

public static class EtransferAliasAddress
{
    // TODO: REMOVE HARD CODED VALUES
    public const string Domain = "emailmoney.app";
    public const int MaximumPrefixLength = 64;

    public static string NormalizePrefix(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            throw new ArgumentException(
                "Please enter an email prefix.");
        }

        var normalized =
            prefix.Trim().ToLowerInvariant();

        var domainSuffix =
            $"@{Domain}";

        if (normalized.EndsWith(
                domainSuffix,
                StringComparison.OrdinalIgnoreCase))
        {
            normalized =
                normalized[..^domainSuffix.Length];
        }

        if (normalized.Length > MaximumPrefixLength)
        {
            throw new ArgumentException(
                $"The email prefix must be less than {MaximumPrefixLength} characters.");
        }

        if (!char.IsLetterOrDigit(normalized[0]) ||
            !char.IsLetterOrDigit(normalized[^1]))
        {
            throw new ArgumentException(
                "The email prefix must start and end with a letter or number.");
        }

        foreach (var character in normalized)
        {
            if (!char.IsLetterOrDigit(character) &&
                character is not '.' and not '-' and not '_')
            {
                throw new ArgumentException(
                    "The email prefix can only contain letters, numbers, periods, hyphens, and underscores.");
            }
        }

        if (normalized.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The email prefix cannot contain consecutive periods.");
        }

        return normalized;
    }

    public static string BuildEmailAddress(
        string? prefix) =>
        $"{NormalizePrefix(prefix)}@{Domain}";

    public static string NormalizeEmailAddress(
        string? emailAddress)
    {
        if (string.IsNullOrWhiteSpace(emailAddress))
        {
            throw new ArgumentException(
                "The donation email address is required.");
        }

        var normalized =
            emailAddress.Trim().ToLowerInvariant();

        var separatorIndex =
            normalized.LastIndexOf('@');

        if (separatorIndex <= 0 ||
            separatorIndex == normalized.Length - 1)
        {
            throw new ArgumentException(
                "The donation email address is invalid.");
        }

        var domain =
            normalized[(separatorIndex + 1)..];

        if (!string.Equals(
                domain,
                Domain,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"The donation email address must use the @{Domain} domain.");
        }

        return BuildEmailAddress(
            normalized[..separatorIndex]);
    }

    public static bool IsManagedAddress(
        string? emailAddress)
    {
        if (string.IsNullOrWhiteSpace(emailAddress))
        {
            return false;
        }

        try
        {
            _ = NormalizeEmailAddress(emailAddress);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}