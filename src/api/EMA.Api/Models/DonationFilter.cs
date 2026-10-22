namespace EMA.Api.Models;

/// <summary>
/// Filter parameters accepted by POST /api/reports/run.
/// Matches the JSON structure defined in the EMA Dashboard filter spec.
/// </summary>
public class DonationFilter
{
    /// <summary>The organization to scope the query to. Required.</summary>
    public int EMAOrganizationID { get; set; }

    /// <summary>
    /// Controls which SQL view is queried.
    /// Accepted values: None, Day, Week, Month, Quarter, Year.
    /// </summary>
    public string? Summarize { get; set; }

    /// <summary>Optional start/end date range applied to DateTimeReceived.</summary>
    public DateRangeFilter? DateRange { get; set; }

    /// <summary>Optional donor name search (partial match).</summary>
    public string? Name { get; set; }

    /// <summary>Optional list of EMAMethodOfGivingID values to include.</summary>
    public List<int>? Methods { get; set; }

    /// <summary>Optional list of EMAFundID values to include.</summary>
    public List<int>? Funds { get; set; }

    /// <summary>Optional list of EMACurrencyTypeID values to include.</summary>
    public List<int>? CurrencyTypes { get; set; }

    /// <summary>Optional list of EMACampusID values to include.</summary>
    public List<int>? Campuses { get; set; }

    /// <summary>Optional min/max donation amount range.</summary>
    public AmountRange? Amount { get; set; }
}

/// <summary>Start and end date boundaries for the Dates filter.</summary>
public class DateRangeFilter
{
    /// <summary>Inclusive start date. Applied as DateTimeReceived >= Start.</summary>
    public DateTime? Start { get; set; }

    /// <summary>Inclusive end date. Applied as DateTimeReceived <= End.</summary>
    public DateTime? End { get; set; }
}

/// <summary>Minimum and maximum donation amount for the Amount filter.</summary>
public class AmountRange
{
    /// <summary>Minimum amount (inclusive). Null means no lower bound.</summary>
    public decimal? Min { get; set; }

    /// <summary>Maximum amount (inclusive). Null means no upper bound.</summary>
    public decimal? Max { get; set; }
}
