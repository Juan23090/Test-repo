namespace EMA.Api.DTOs;

public class OrganizationDTO
{
    public int EMAOrganizationID { get; set; }

    public string? AdminName { get; set; }
    public string? AdminEmail { get; set; }
    public char? AdminEmailVerified { get; set; }
    public string? AdminPhone { get; set; }
    public char? AdminPhoneVerified { get; set; }

    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public char? ContactEmailVerified { get; set; }
    public string? ContactPhone { get; set; }

    public string? OrganizationName { get; set; }
    public string? OrganizationPhone { get; set; }
    public string? OrganizationEmail { get; set; }
    public char? OrganizationEmailVerified { get; set; }
    public string? OrganizationWebsite { get; set; }

    public string? LogoBlobID { get; set; }

    public string? Address { get; set; }
    public string? City { get; set; }
    public string? ProvinceStateCode { get; set; }
    public string? PostalCode { get; set; }

    public string? EtransferEmailAddress { get; set; }
    public char? EtransferEmailVerified { get; set; }

    public string? AdminEmailVerificationToken { get; set; }
    public string? OrganizationEmailVerificationToken { get; set; }
    public string? ContactEmailVerificationToken { get; set; }
    public string? EtransferEmailVerificationToken { get; set; }

    public string? CRARegisteredCharityNumber { get; set; }

    public int? ChMSID { get; set; }
    public int? AccountingSoftwareID { get; set; }
    public string? DateFormatID { get; set; }

    // Deposit slip configuration (set via the Set up Wizard; consumed by /deposit-slip).
    public string? BankBranchNumber { get; set; }
    public string? BankAccountNumber { get; set; }
    public int? EMAMethodOfGivingIDForCheques { get; set; }
    public int? EMAMethodOfGivingIDForCash { get; set; }
    public string? CountingSteps { get; set; }

    public string? AnnualBudget { get; set; }

    public int? OrganizationStatusID { get; set; }

    public int? EMAEventIDForPADs { get; set; }
    public int? EMASourceIDForPADs { get; set; }
    public int? EMAMethodOfGivingIDForPADs { get; set; }
    public int? EMAFundIDForPADs { get; set; }
    public int? EMACampusIDForPADs { get; set; }

    public DateTime? DateTimeUpdated { get; set; }
    public int? UpdatedByUserID { get; set; }

    public string? ETLDonorDataBlobID { get; set; }
    public string? ETLDonationDataBlobID { get; set; }

    public DateTime? DateTimeCreated { get; set; }
    public int? CreatedByUserID { get; set; }
}