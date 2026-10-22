using System.Globalization;

namespace EMA.App.Utilities;

/// <summary>
/// Formats dates using the organization's configured DateFormatID (a real .NET format string,
/// e.g. "yyyy MMM dd" / "dd MMM yyyy" / "yyyy/MM/dd"). Falls back to ISO (yyyy-MM-dd) when the
/// format is missing or invalid. Reports honour Organization.DateFormatID via this helper.
/// </summary>
public static class OrgDateFormat
{
    private const string Fallback = "yyyy-MM-dd";

    public static string Format(DateTime date, string? formatId)
    {
        var fmt = string.IsNullOrWhiteSpace(formatId) ? Fallback : formatId!.Trim();
        try
        {
            return date.ToString(fmt, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return date.ToString(Fallback, CultureInfo.InvariantCulture);
        }
    }

    public static string Format(DateOnly date, string? formatId)
        => Format(date.ToDateTime(TimeOnly.MinValue), formatId);

    /// <summary>
    /// Formats a date supplied as a string (typically ISO "yyyy-MM-dd" from an API). Returns the
    /// original string unchanged if it can't be parsed, and empty for null/blank.
    /// </summary>
    public static string Format(string? isoDate, string? formatId)
    {
        if (string.IsNullOrWhiteSpace(isoDate))
            return string.Empty;

        return DateTime.TryParse(isoDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? Format(parsed, formatId)
            : isoDate!;
    }
}
