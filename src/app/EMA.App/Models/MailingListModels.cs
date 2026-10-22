namespace EMA.App.Models;

public class MailingListItem
{
    public int MailingListID { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string LastInstance { get; set; } = "N/A";
    public int SendListCount { get; set; }
    public bool IsActive { get; set; } = true;
}

public class MailingListDetailsModel
{
    public int MailingListID { get; set; }
    public int EMAOrganizationID { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime? Donation1StartDate { get; set; }
    public DateTime? Donation1EndDate { get; set; }
    public decimal? Donation1MoneyFrom { get; set; }
    public decimal? Donation1MoneyTo { get; set; }
    public string? AndOr { get; set; }
    public DateTime? Donation2StartDate { get; set; }
    public DateTime? Donation2EndDate { get; set; }
    public decimal? Donation2MoneyFrom { get; set; }
    public decimal? Donation2MoneyTo { get; set; }
    public string OutputFormatName { get; set; } = "CSV";
    public string SendMethodName { get; set; } = "Email";
    public bool IsActive { get; set; } = true;
    public DateTime DateTimeCreated { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? DateTimeUpdated { get; set; }
    public string? UpdatedBy { get; set; }
    public List<MailingListInstanceHistory> Instances { get; set; } = new();
}

public class MailingListInstanceHistory
{
    public int MailingListInstanceID { get; set; }
    public DateTime DateTimeCreated { get; set; }
    public int Selected { get; set; }
    public int OptedIn { get; set; }
    public int OptedOut { get; set; }
    public int SendList { get; set; }
}

public class ReviewDonorItem
{
    public int EMADonorID { get; set; }
    public string OfficialDonorName { get; set; } = string.Empty;
    public string? LastDonationDate { get; set; }
    public decimal? LastDonationAmount { get; set; }
    public bool Selected { get; set; }
    public bool OptedIn { get; set; }
    public bool OptedOut { get; set; }
    public bool MailingList { get; set; }
}

public class CreateMailingListModel
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime? Donation1StartDate { get; set; }
    public DateTime? Donation1EndDate { get; set; }
    public decimal? Donation1MoneyFrom { get; set; }
    public decimal? Donation1MoneyTo { get; set; }
    public string AndOr { get; set; } = "And";
    public DateTime? Donation2StartDate { get; set; }
    public DateTime? Donation2EndDate { get; set; }
    public decimal? Donation2MoneyFrom { get; set; }
    public decimal? Donation2MoneyTo { get; set; }
    public int OutputFormatID { get; set; } = 1;
    public int SendMethodID { get; set; } = 1;
    public bool IsActive { get; set; } = true;
}
