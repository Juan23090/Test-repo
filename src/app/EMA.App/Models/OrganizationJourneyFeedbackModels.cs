namespace EMA.App.Models;

public sealed class OrganizationJourneyFeedbackSummary
{
    public List<OrganizationJourneyFaq> Faqs { get; set; } = new();
    public List<OrganizationJourneyEnhancement> Enhancements { get; set; } = new();
    public List<OrganizationJourneySupportTicket> SupportTickets { get; set; } = new();
}

public sealed class OrganizationJourneyFaq
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

public sealed class OrganizationJourneyEnhancement
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

public sealed class OrganizationJourneySupportTicket
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
