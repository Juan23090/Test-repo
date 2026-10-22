using System.Text.Json.Serialization;

namespace EMA.Api.Models;

/// <summary>
/// Result of checking an organization's eligibility to refer peers.
/// </summary>
public sealed class ReferralEligibilityDto
{
    [JsonPropertyName("isEligible")]
    public bool IsEligible { get; set; }

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonPropertyName("daysActive")]
    public int DaysActive { get; set; }

    [JsonPropertyName("donationCount")]
    public int DonationCount { get; set; }

    [JsonPropertyName("defaultPreface")]
    public string DefaultPreface { get; set; } = string.Empty;

    [JsonPropertyName("emailBody")]
    public string EmailBody { get; set; } = string.Empty;
}

/// <summary>
/// Individual recipient for a batch referral email.
/// </summary>
public sealed class SendReferralRecipientDto
{
    [JsonPropertyName("organizationName")]
    public string OrganizationName { get; set; } = string.Empty;

    [JsonPropertyName("contactName")]
    public string ContactName { get; set; } = string.Empty;

    [JsonPropertyName("emailAddress")]
    public string EmailAddress { get; set; } = string.Empty;
}

/// <summary>
/// Request payload for sending batch referral emails.
/// </summary>
public sealed class SendReferralsRequest
{
    [JsonPropertyName("prefaceText")]
    public string PrefaceText { get; set; } = string.Empty;

    [JsonPropertyName("recipients")]
    public List<SendReferralRecipientDto> Recipients { get; set; } = new();
}

/// <summary>
/// Individual outcome for one referral recipient.
/// </summary>
public sealed class SendReferralItemResult
{
    [JsonPropertyName("emailAddress")]
    public string EmailAddress { get; set; } = string.Empty;

    [JsonPropertyName("organizationName")]
    public string OrganizationName { get; set; } = string.Empty;

    [JsonPropertyName("isSuccess")]
    public bool IsSuccess { get; set; }

    [JsonPropertyName("referralID")]
    public long? ReferralID { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Batch result of sending referral emails.
/// </summary>
public sealed class SendReferralsResult
{
    [JsonPropertyName("totalRequested")]
    public int TotalRequested { get; set; }

    [JsonPropertyName("sentCount")]
    public int SentCount { get; set; }

    [JsonPropertyName("failedCount")]
    public int FailedCount { get; set; }

    [JsonPropertyName("results")]
    public List<SendReferralItemResult> Results { get; set; } = new();
}

/// <summary>
/// Request to accept/claim a referral for a newly registered organization.
/// </summary>
public sealed class AcceptReferralRequest
{
    [JsonPropertyName("referralCode")]
    public string ReferralCode { get; set; } = string.Empty;

    [JsonPropertyName("referralEmail")]
    public string ReferralEmail { get; set; } = string.Empty;
}

/// <summary>
/// Result of claiming a referral.
/// </summary>
public sealed class AcceptReferralResult
{
    [JsonPropertyName("isSuccess")]
    public bool IsSuccess { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("referralID")]
    public long? ReferralID { get; set; }

    [JsonPropertyName("referringOrgID")]
    public int? ReferringOrgID { get; set; }

    [JsonPropertyName("referringOrgName")]
    public string? ReferringOrgName { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

public sealed class ReferralHistoryItemDto
{
    [JsonPropertyName("referralID")]
    public long ReferralID { get; set; }

    /// <summary>
    /// True when the requesting organization sent this referral, false when it
    /// received it. Summary totals count only the referrals it sent.
    /// </summary>
    [JsonPropertyName("isReferrer")]
    public bool IsReferrer { get; set; }

    [JsonPropertyName("dateReferred")]
    public DateTime DateReferred { get; set; }

    [JsonPropertyName("referringOrganization")]
    public string ReferringOrganization { get; set; } = string.Empty;

    [JsonPropertyName("referredByUser")]
    public string ReferredByUser { get; set; } = string.Empty;

    [JsonPropertyName("referredTo")]
    public string ReferredTo { get; set; } = string.Empty;

    [JsonPropertyName("emailAddress")]
    public string EmailAddress { get; set; } = string.Empty;

    [JsonPropertyName("recipientOrganization")]
    public string RecipientOrganization { get; set; } = string.Empty;

    [JsonPropertyName("recipientUser")]
    public string RecipientUser { get; set; } = string.Empty;

    [JsonPropertyName("dateAccepted")]
    public DateTime? DateAccepted { get; set; }

    [JsonPropertyName("dateCredited")]
    public DateTime? DateCredited { get; set; }

    [JsonPropertyName("referringAmount")]
    public decimal? ReferringAmount { get; set; }

    [JsonPropertyName("recipientAmount")]
    public decimal? RecipientAmount { get; set; }

    [JsonPropertyName("creditAmount")]
    public decimal? CreditAmount { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "Pending";
}

/// <summary>
/// Summary and history of referrals for an organization.
/// </summary>
public sealed class ReferralSummaryDto
{
    [JsonPropertyName("totalSent")]
    public int TotalSent { get; set; }

    [JsonPropertyName("totalAccepted")]
    public int TotalAccepted { get; set; }

    [JsonPropertyName("totalCredited")]
    public int TotalCredited { get; set; }

    [JsonPropertyName("accumulatedCreditBalance")]
    public decimal AccumulatedCreditBalance { get; set; }

    [JsonPropertyName("history")]
    public List<ReferralHistoryItemDto> History { get; set; } = new();
}
