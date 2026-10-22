using System.Collections.Generic;

namespace EMA.App.Models;

/// <summary>
/// Cross-tab EMA Statistics payload: rows are organizations/ChMS groups, columns are time
/// periods. Matches the JSON returned by GetEmaStatistics ({ periods, rows }).
/// </summary>
public sealed class EmaStatisticsResult
{
    /// <summary>Column headers (time periods), newest first, max 13.</summary>
    public List<string> Periods { get; set; } = new();

    public List<EmaStatisticsRow> Rows { get; set; } = new();
}

public sealed class EmaStatisticsRow
{
    /// <summary>Row label — the organization or ChMS name.</summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>Period label → measurement value for this group.</summary>
    public Dictionary<string, decimal> Values { get; set; } = new();
}
