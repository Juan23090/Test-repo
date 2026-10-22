namespace EMA.Api.DTOs;

public sealed class DonationDTO
{
    public int EMAOrganizationID { get; set; }

    public int EMADonorID { get; set; }

    public long EMADonationID { get; set; }

    public string? InteracDonorName { get; set; }

    public string? EmailBlobPath { get; set; }

    public string? Comments { get; set; }

    public DateTime? DateTimeReceived { get; set; }

    public string? DonorToOrgChangeRequest { get; set; }

    public int? EMADonationSourceID { get; set; }

    public int? EMAMethodOfGivingID { get; set; }

    public int? EMAEventID { get; set; }

    public string? GlobalMessageID { get; set; }

    public string? ReferenceNumber { get; set; }

    public decimal DonationAmount { get; set; }

    public string? TributeText { get; set; }

    public int? EMATributeTypeID { get; set; }

    public char? IsTemplate { get; set; }

    public bool ClearInteracDonorName { get; set; }

    public string? RandomNumber2FA { get; set; }

    public DateTime? DateTime2FASent { get; set; }

    public DateTime? DateTime2FAReceived { get; set; }

    public DateTime? DateTime2FAExpires { get; set; }

    public DateTime? DateTimeDonationLocked { get; set; }

    public DateTime? DateTimeCreated { get; set; }

    public int? CreatedbyUserID { get; set; }

    public DateTime? DateTimeUpdated { get; set; }

    public int? UpdatedbyUserID { get; set; }

    public List<DonationDistributionDTO> Distributions { get; set; } = [];
}

public sealed class DonationDistributionDTO
{
    public long EMADonationID { get; set; }

    public long EMADonationDistributionID { get; set; }

    public int EMACampusID { get; set; }

    public int EMAFundID { get; set; }

    public decimal DistributionAmount { get; set; }
}