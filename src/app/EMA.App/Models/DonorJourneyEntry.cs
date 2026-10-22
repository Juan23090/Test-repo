using System;
using System.Text.Json.Serialization;

namespace EMA.App.Models;

public class DonorJourneyEntry
{
    public int EMAOrganizationID { get; set; }
    public int EMADonorId { get; set; }

    [JsonPropertyName("userID")]
    public int UserId { get; set; }

    public string? DonorInteractionId { get; set; }
    public DateTime ActivityDateTIme { get; set; }
    public int ActivityTypeID { get; set; }
    public string? ActivityComments { get; set; }
    public int? DonorStatusID { get; set; }

    [JsonPropertyName("followUpDateTime")]
    public DateTime? FollowUpDateTIme { get; set; }

    public int? FollowUpActivityTypeID { get; set; }
}
