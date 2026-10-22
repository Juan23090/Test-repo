using System.Data;

namespace EMA.Api.Services.Donors;

/*
    Normalization for the donor 'Y'/'N' flags.

    Kept as a pure static class rather than inline in the functions so the
    create-versus-update semantics can be unit tested directly - these flags
    change email-ingestion routing, so getting them wrong silently reroutes a
    donor's mail.

    The two paths deliberately differ:

      Create - an unrecognized value falls back to the column default, since
               there is no prior value to preserve.

      Update - an unrecognized value becomes NULL, and the stored procedure
               keeps whatever is already on the row. An older or partial
               caller that omits the field must not be able to reset a flag
               it does not know about.
*/
public static class DonorFlagNormalizer
{
    public const string Yes = "Y";
    public const string No = "N";

    /// <summary>
    /// Returns "Y" or "N" for a recognized value, otherwise
    /// <paramref name="fallback"/>. Used on insert, where the column
    /// default is the correct answer for an absent field.
    /// </summary>
    public static string NormalizeForCreate(
        string? value,
        string fallback)
    {
        if (fallback is not (Yes or No))
        {
            throw new ArgumentOutOfRangeException(
                nameof(fallback),
                fallback,
                "The fallback must be \"Y\" or \"N\".");
        }

        return Match(value) ?? fallback;
    }

    /// <summary>
    /// Returns "Y" or "N" for a recognized value, otherwise
    /// <see cref="DBNull.Value"/> so the stored procedure preserves the
    /// donor's existing value. Used on update.
    /// </summary>
    public static object NormalizeForUpdate(
        string? value)
    {
        return Match(value) ?? (object)DBNull.Value;
    }

    private static string? Match(
        string? value)
    {
        var trimmed = value?.Trim();

        if (string.Equals(
                trimmed,
                Yes,
                StringComparison.OrdinalIgnoreCase))
        {
            return Yes;
        }

        if (string.Equals(
                trimmed,
                No,
                StringComparison.OrdinalIgnoreCase))
        {
            return No;
        }

        return null;
    }
}
