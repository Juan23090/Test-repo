using System;

namespace EMA.App.Models;

public class OrganizationFollowUpItem
{
    public int OrganizationInteractionID { get; set; }
    public int EMAOrganizationID { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public string ActivityDescription { get; set; } = string.Empty;
    public DateTime FollowUpDateTime { get; set; }
    public string StatusDescription { get; set; } = string.Empty;
}
