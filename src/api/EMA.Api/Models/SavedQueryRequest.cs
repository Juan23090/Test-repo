namespace EMA.Api.Models;

/// <summary>Request body for POST /api/savedqueries.</summary>
public class SavedQueryRequest
{
    /// <summary>Organization to scope the query to. Required.</summary>
    public int EMAOrganizationID { get; set; }

    /// <summary>Display name for the saved query. Required. Used as the primary key alongside EMAOrganizationID.</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// JSON-serialised filter state matching the DonationFilter shape.
    /// Stored verbatim in dbo.OrganizationDashboard.Filters.
    /// </summary>
    public string Filters { get; set; } = "{}";
}
