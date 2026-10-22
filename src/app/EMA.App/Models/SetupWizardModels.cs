namespace EMA.App.Models;

public sealed class SetupWizardResult
{
    public int EMAOrganizationID { get; set; }

    public string OrganizationName { get; set; } = string.Empty;

    public List<SetupWizardStep> Steps { get; set; } = new();
}

public sealed class SetupWizardStep
{
    public int MenuItemID { get; set; }

    public string MenuName { get; set; } = string.Empty;

    public string ScreenTitle { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public long VisitCount { get; set; }

    public DateTime? LastVisitUtc { get; set; }
}
