namespace EMA.App.Models;

public class PADCustomizationData
{
    public char UseEvents { get; set; } = 'Y';
    public char UseSources { get; set; } = 'Y';
    public char UseMethodsOfGiving { get; set; } = 'Y';
    public char UseFunds { get; set; } = 'Y';
    public char UseCampuses { get; set; } = 'Y';

    public int? EMAEventIDForPADs { get; set; }
    public int? EMASourceIDForPADs { get; set; }
    public int? EMAMethodOfGivingIDForPADs { get; set; }
    public int? EMAFundIDForPADs { get; set; }
    public int? EMACampusIDForPADs { get; set; }

    public List<EventItem> Events { get; set; } = new();
    public List<DonationSourceItem> DonationSources { get; set; } = new();
    public List<MethodOfGivingItem> MethodsOfGiving { get; set; } = new();
    public List<FundItem> Funds { get; set; } = new();
    public List<CampusItem> Campuses { get; set; } = new();
}
