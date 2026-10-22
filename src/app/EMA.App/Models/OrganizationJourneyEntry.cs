using System;
using System.Text.Json.Serialization;

namespace EMA.App.Models;

public class OrganizationJourneyEntry
{
    public int OrganizationInteractionID { get; set; }
    public int EMAOrganizationID { get; set; }

    [JsonPropertyName("userID")]
    public int UserId { get; set; }

    public string? UserName { get; set; }

    public int? MenuItemID { get; set; }
    public int? EnhancementRequestID { get; set; }
    public int? FAQID { get; set; }
    public int? SupportTicketID { get; set; }

    [JsonPropertyName("activityDateTime")]
    public DateTime ActivityDateTime { get; set; }

    [JsonPropertyName("activityTypeID")]
    public int ActivityTypeID { get; set; }

    public string? ActivityDescription { get; set; }

    [JsonPropertyName("interactionComments")]
    public string? InteractionComments { get; set; }

    [JsonPropertyName("organizationStatusID")]
    public int? OrganizationStatusID { get; set; }

    public string? OrganizationStatusDescription { get; set; }

    [JsonPropertyName("followUpDateTime")]
    public DateTime? FollowUpDateTime { get; set; }

    [JsonPropertyName("followUpActivityTypeID")]
    public int? FollowUpActivityTypeID { get; set; }
}
