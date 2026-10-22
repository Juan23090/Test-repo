namespace EMA.App.Models;

public class OrganizationJourneyActivityType
{
    public int OrganizationJournalActivityTypeID { get; set; }
    public int Sequence { get; set; }
    public string ActivityDescription { get; set; } = string.Empty;
    public string IsActive { get; set; } = "Y";
}
