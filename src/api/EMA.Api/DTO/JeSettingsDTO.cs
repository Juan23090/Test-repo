namespace EMA.Api.DTOs;

/// <summary>
/// Journal Entry screen settings for an organization, plus the resolved accounting-software
/// name and the org's preferred date format. GET returns all fields; PUT reads only
/// JESummarizeBy + JEByDate.
/// </summary>
public class JeSettingsDTO
{
    public string? JESummarizeBy { get; set; }          // 'A' = Account, 'D' = Description
    public string? JEByDate { get; set; }               // 'Y' / 'N'
    public int? AccountingSoftwareID { get; set; }
    public string? AccountingSoftwareName { get; set; }
    public string? DateFormat { get; set; }
}
