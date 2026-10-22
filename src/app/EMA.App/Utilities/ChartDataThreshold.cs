namespace EMA.App.Utilities;

/// <summary>
/// Collapses the long tail of a Fund/Method bar chart. Entries worth less than 1% of the
/// total dollar value are summed into a single trailing "Other" bar, so the chart stays
/// readable without hiding money — the bars still add up to the full total.
/// </summary>
public static class ChartDataThreshold
{
    private const decimal MinimumShare = 0.01m;

    public const string OtherLabel = "Other";

    public static (string[] Labels, decimal[] Amounts) GroupSmallEntries(
        IEnumerable<(string Label, decimal Amount)> items)
    {
        var ordered = items
            .OrderByDescending(i => i.Amount)
            .ToList();

        var total = ordered.Sum(i => i.Amount);

        // Nothing meaningful to compare against — show what we have, biggest first.
        if (total <= 0m)
        {
            return (
                ordered.Select(i => i.Label).ToArray(),
                ordered.Select(i => i.Amount).ToArray());
        }

        var threshold = total * MinimumShare;

        var kept = ordered.Where(i => i.Amount >= threshold).ToList();
        var otherTotal = ordered.Where(i => i.Amount < threshold).Sum(i => i.Amount);

        var labels = kept.Select(i => i.Label).ToList();
        var amounts = kept.Select(i => i.Amount).ToList();

        if (otherTotal > 0m)
        {
            labels.Add(OtherLabel);
            amounts.Add(otherTotal);
        }

        return (labels.ToArray(), amounts.ToArray());
    }
}
