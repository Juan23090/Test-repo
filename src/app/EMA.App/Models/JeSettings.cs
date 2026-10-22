namespace EMA.App.Models;

/// <summary>
/// Journal Entry screen settings for an organization, returned by GET
/// /api/organization/{orgID}/je-settings. JESummarizeBy is 'A'/'D'; JEByDate is 'Y'/'N'.
/// </summary>
public class JeSettings
{
    public string? JESummarizeBy { get; set; }
    public string? JEByDate { get; set; }
    public int? AccountingSoftwareID { get; set; }
    public string? AccountingSoftwareName { get; set; }
    public string? DateFormat { get; set; }
}
