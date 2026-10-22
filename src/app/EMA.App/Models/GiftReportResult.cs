using System.Collections.Generic;

namespace EMA.App.Models;

/// <summary>
/// Cross-tab gift report payload: rows are periods, columns are classification values.
/// Matches the JSON returned by GetGiftReport (periods, classifications, rows, grandTotal).
/// </summary>
public sealed class GiftReportResult
{
    public List<string> Periods { get; set; } = new();
    public List<string> Classifications { get; set; } = new();
    public List<GiftReportRow> Rows { get; set; } = new();
    public decimal GrandTotal { get; set; }
}

public sealed class GiftReportRow
{
    public string PeriodDate { get; set; } = string.Empty;
    public decimal Total { get; set; }

    /// <summary>Classification name → value for this period (TotalDonations / DonationCount / AverageGiftSize).</summary>
    public Dictionary<string, decimal> Values { get; set; } = new();
}
