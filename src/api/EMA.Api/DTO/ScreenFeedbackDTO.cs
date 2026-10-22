namespace EMA.Api.DTOs;

public sealed class ScreenActionSummaryDto
{
    public int MenuItemID { get; set; }
    public List<ScreenFaqDto> Faqs { get; set; } = new();
    public List<ScreenEnhancementDto> Enhancements { get; set; } = new();
    public List<ScreenSupportTicketDto> SupportTickets { get; set; } = new();
    public string? EnhancementIconColor { get; set; }
}

public sealed class ScreenFaqDto
{
    public int FAQID { get; set; }
    public int MenuItemID { get; set; }
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
}

public sealed class ScreenEnhancementDto
{
    public int EnhancementRequestID { get; set; }
    public int MenuItemID { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public short EnhancementRequestStatusID { get; set; }
    public string StatusDescription { get; set; } = string.Empty;
    public string IconColor { get; set; } = string.Empty;
    public short MaxDaysAtStatus { get; set; }
    public DateTime DateTimeUpdated { get; set; }
    public int YesVotes { get; set; }
    public int NoVotes { get; set; }
    public string? OrganizationVote { get; set; }
}

public sealed class ScreenSupportTicketDto
{
    public int SupportTicketID { get; set; }
    public int MenuItemID { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public short SupportTicketStatusID { get; set; }
    public string StatusDescription { get; set; } = string.Empty;
    public DateTime DateTimeUpdated { get; set; }
}


public sealed class OrganizationJourneyFeedbackSummaryDto
{
    public List<OrganizationJourneyFaqDto> Faqs { get; set; } = new();
    public List<OrganizationJourneyEnhancementDto> Enhancements { get; set; } = new();
    public List<OrganizationJourneySupportTicketDto> SupportTickets { get; set; } = new();
}

public sealed class OrganizationJourneyFaqDto
{
    public int FAQID { get; set; }
    public int? MenuItemID { get; set; }
    public int? OrganizationInteractionID { get; set; }
    public string ScreenTitle { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public bool IsReviewed { get; set; }
    public bool IsActive { get; set; }
    public DateTime DateTimeEntered { get; set; }
}

public sealed class OrganizationJourneyEnhancementDto
{
    public int EnhancementRequestID { get; set; }
    public int MenuItemID { get; set; }
    public int? OrganizationInteractionID { get; set; }
    public string ScreenTitle { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string StatusDescription { get; set; } = string.Empty;
    public DateTime DateTimeEntered { get; set; }
}

public sealed class OrganizationJourneySupportTicketDto
{
    public int SupportTicketID { get; set; }
    public int MenuItemID { get; set; }
    public int? OrganizationInteractionID { get; set; }
    public string ScreenTitle { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string StatusDescription { get; set; } = string.Empty;
    public DateTime DateTimeEntered { get; set; }
}

public sealed class CreateFaqRequestDto
{
    public string Question { get; set; } = string.Empty;
    public string? Answer { get; set; }
}

public sealed class CreateEnhancementRequestDto
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class CreateSupportTicketRequestDto
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class SaveEnhancementVoteRequestDto
{
    public string Vote { get; set; } = string.Empty;
}

public sealed class CreateScreenFeedbackResponseDto
{
    public int ID { get; set; }
}

public sealed class PublicFaqDto
{
    public int FAQID { get; set; }
    public int? MenuItemID { get; set; }
    public string ScreenTitle { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public DateTime? DateTimeUpdated { get; set; }
}

public sealed class FaqAdminDto
{
    public int FAQID { get; set; }
    public int? MenuItemID { get; set; }
    public string ScreenTitle { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public string? Comments { get; set; }
    public bool IsReviewed { get; set; }
    public bool IsActive { get; set; }
    public bool CopyToPublic { get; set; }
    public DateTime DateTimeEntered { get; set; }
    public DateTime? DateTimeUpdated { get; set; }
    public int AskedByUserID { get; set; }
    public string AskedByUserName { get; set; } = string.Empty;
    public int? OrganizationID { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public int? UpdatedByUserID { get; set; }
    public string UpdatedByUserName { get; set; } = string.Empty;
}

public sealed class UpdateFaqAdminRequestDto
{
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public string? Comments { get; set; }
    public bool IsReviewed { get; set; }
    public bool IsActive { get; set; }
    public bool CopyToPublic { get; set; }
}

public sealed class PublicFaqPublishResponseDto
{
    public int PublishedFaqCount { get; set; }
    public DateTime PublishedAtUtc { get; set; }
}

public sealed class EnhancementStatusDto
{
    public short EnhancementRequestStatusID { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool ShowIcon { get; set; }
    public string IconColor { get; set; } = string.Empty;
    public short MaxDaysAtStatus { get; set; }
}

public sealed class EnhancementAdminDto
{
    public int EnhancementRequestID { get; set; }
    public int MenuItemID { get; set; }
    public string ScreenTitle { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? EMAComments { get; set; }
    public short EnhancementRequestStatusID { get; set; }
    public string StatusDescription { get; set; } = string.Empty;
    public bool ShowIcon { get; set; }
    public string IconColor { get; set; } = string.Empty;
    public short MaxDaysAtStatus { get; set; }
    public DateTime DateTimeEntered { get; set; }
    public DateTime DateTimeUpdated { get; set; }
    public int EnteredByUserID { get; set; }
    public string EnteredByUserName { get; set; } = string.Empty;
    public int? OrganizationID { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public int UpdatedByUserID { get; set; }
    public string UpdatedByUserName { get; set; } = string.Empty;
    public int YesVotes { get; set; }
    public int NoVotes { get; set; }
}

public sealed class EnhancementAdminResponseDto
{
    public List<EnhancementStatusDto> Statuses { get; set; } = new();
    public List<EnhancementAdminDto> Items { get; set; } = new();
}

public sealed class UpdateEnhancementAdminRequestDto
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? EMAComments { get; set; }
    public short EnhancementRequestStatusID { get; set; }
}

public sealed class SupportTicketStatusDto
{
    public short SupportTicketStatusID { get; set; }
    public string Description { get; set; } = string.Empty;
}

public sealed class SupportTicketAdminDto
{
    public int SupportTicketID { get; set; }
    public int MenuItemID { get; set; }
    public string ScreenTitle { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? EMAComments { get; set; }
    public short SupportTicketStatusID { get; set; }
    public string StatusDescription { get; set; } = string.Empty;
    public DateTime DateTimeEntered { get; set; }
    public DateTime DateTimeUpdated { get; set; }
    public int RequestedByUserID { get; set; }
    public string RequestedByUserName { get; set; } = string.Empty;
    public int? OrganizationID { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public int UpdatedByEMAUserID { get; set; }
    public string UpdatedByUserName { get; set; } = string.Empty;
}

public sealed class SupportTicketAdminResponseDto
{
    public List<SupportTicketStatusDto> Statuses { get; set; } = new();
    public List<SupportTicketAdminDto> Items { get; set; } = new();
}

public sealed class UpdateSupportTicketAdminRequestDto
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? EMAComments { get; set; }
    public short SupportTicketStatusID { get; set; }
}
