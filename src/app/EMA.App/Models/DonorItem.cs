namespace EMA.App.Models;

public class DonorItem
{
    public int EMADonorID { get; set; }
    public int EMAOrganizationID { get; set; }
    public string? ChMSDonorID { get; set; }
    public int? EMADefaultCampusID { get; set; }
    public string? EmailAddress { get; set; }
    public string? GivingNumber { get; set; }
    public string? InteracDonorName { get; set; }
    public string? OfficialDonorName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? City { get; set; }
    public string? ProvinceStateCode { get; set; }
    public string? Country { get; set; }
    public string? PostalCode { get; set; }
    public string? Address { get; set; }
    public bool? IsActive { get; set; }
    public DateTime? DateTimeCreated { get; set; }
    public int? CreatedByUserID { get; set; }
    public DateTime? DateTimeUpdated { get; set; }
    public int? UpdatedbyUserID { get; set; }

    /// <summary>
    /// Email Ingestion Pilot: "Y" when this donor's eTransfer emails
    /// follow the normal ingestion flow while the organization is
    /// piloting. Anything else means the email is forwarded to the
    /// organization admin instead.
    /// </summary>
    public string? IsDonorPilotingEtransfers { get; set; }
    public string? AcknowledgmentForManualTransactions { get; set; }
    public string? AcknowledgementForAutomatedTransactions { get; set; }

    public int? DonorStatusID { get; set; }
}