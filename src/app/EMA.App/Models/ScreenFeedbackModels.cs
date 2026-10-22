namespace EMA.App.Models;

public enum ScreenEntryKind
{
    Faq = 1,
    Enhancement = 2,
    Support = 3
}

public sealed record ScreenEntryRequest(
    ScreenEntryKind Kind,
    int MenuItemID);

public sealed class ScreenActionSummary
{
    public int MenuItemID { get; set; }
    public List<ScreenFaq> Faqs { get; set; } = new();
    public List<ScreenEnhancement> Enhancements { get; set; } = new();
    public List<ScreenSupportTicket> SupportTickets { get; set; } = new();
    public string? EnhancementIconColor { get; set; }
}

public sealed class ScreenFaq
{
    public int FAQID { get; set; }
    public int MenuItemID { get; set; }
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
}

public sealed class ScreenEnhancement
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

public sealed class ScreenSupportTicket
{
    public int SupportTicketID { get; set; }
    public int MenuItemID { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public short SupportTicketStatusID { get; set; }
    public string StatusDescription { get; set; } = string.Empty;
    public DateTime DateTimeUpdated { get; set; }
}

public sealed class CreateFaqRequest
{
    public string Question { get; set; } = string.Empty;
    public string? Answer { get; set; }
}

public sealed class CreateEnhancementRequest
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class CreateSupportTicketRequest
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class SaveEnhancementVoteRequest
{
    public string Vote { get; set; } = string.Empty;
}

public sealed class CreateScreenFeedbackResponse
{
    public int ID { get; set; }
}

public sealed class PublicFaq
{
    public int FAQID { get; set; }
    public int? MenuItemID { get; set; }
    public string ScreenTitle { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public DateTime? DateTimeUpdated { get; set; }
}

public sealed class FaqAdminItem
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

public sealed class UpdateFaqAdminRequest
{
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public string? Comments { get; set; }
    public bool IsReviewed { get; set; }
    public bool IsActive { get; set; }
    public bool CopyToPublic { get; set; }
}

public sealed class PublicFaqPublishResponse
{
    public int PublishedFaqCount { get; set; }
    public DateTime PublishedAtUtc { get; set; }
}

public sealed class EnhancementStatus
{
    public short EnhancementRequestStatusID { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool ShowIcon { get; set; }
    public string IconColor { get; set; } = string.Empty;
    public short MaxDaysAtStatus { get; set; }
}

public sealed class EnhancementAdminItem
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

public sealed class EnhancementAdminResponse
{
    public List<EnhancementStatus> Statuses { get; set; } = new();
    public List<EnhancementAdminItem> Items { get; set; } = new();
}

public sealed class UpdateEnhancementAdminRequest
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? EMAComments { get; set; }
    public short EnhancementRequestStatusID { get; set; }
}

public sealed class SupportTicketStatus
{
    public short SupportTicketStatusID { get; set; }
    public string Description { get; set; } = string.Empty;
}

public sealed class SupportTicketAdminItem
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

public sealed class SupportTicketAdminResponse
{
    public List<SupportTicketStatus> Statuses { get; set; } = new();
    public List<SupportTicketAdminItem> Items { get; set; } = new();
}

public sealed class UpdateSupportTicketAdminRequest
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? EMAComments { get; set; }
    public short SupportTicketStatusID { get; set; }
}
