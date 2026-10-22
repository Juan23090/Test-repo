namespace EMA.App.Models;

public sealed class OrganizationOnboardingSettings
{
    public int EMAOrganizationID { get; set; }

    public bool UseAnonymous { get; set; }
    public bool UseCampuses { get; set; }
    public bool UseEvents { get; set; }
    public bool UseFunds { get; set; }
    public bool UseMethodsOfGiving { get; set; }
    public bool UseSources { get; set; }
    public bool UseTributeTypes { get; set; }

    public int? EMADonorIDForAnonymous { get; set; }

    public string? BankBranchNumber { get; set; }
    public string? BankAccountNumber { get; set; }
    public int? EMAMethodOfGivingIDForCheques { get; set; }
    public int? EMAMethodOfGivingIDForCash { get; set; }

    public int? EMAEventIDForEtransfers { get; set; }
    public int? EMASourceIDForEtransfers { get; set; }
    public int? EMAMethodOfGivingIDForETransfers { get; set; }
    public int? EMAFundIDForEtransfer { get; set; }
    public int? EMACampusIDForEtransfers { get; set; }

    public int? EMAEventIDForPADs { get; set; }
    public int? EMASourceIDForPADs { get; set; }
    public int? EMAMethodOfGivingIDForPADs { get; set; }
    public int? EMAFundIDForPADs { get; set; }
    public int? EMACampusIDForPADs { get; set; }

    public bool CreditDebitSchemaAvailable { get; set; }
    public int? EMAEventIDForCreditDebitCards { get; set; }
    public int? EMASourceIDForCreditDebitCards { get; set; }
    public int? EMAMethodOfGivingIDForCreditDebitCards { get; set; }
    public int? EMAFundIDForCreditDebitCards { get; set; }
    public int? EMACampusIDForCreditDebitCards { get; set; }
}

public sealed class SaveOrganizationOnboardingSettingsRequest
{
    public int? EMADonorIDForAnonymous { get; set; }

    public string? BankBranchNumber { get; set; }
    public string? BankAccountNumber { get; set; }
    public int? EMAMethodOfGivingIDForCheques { get; set; }
    public int? EMAMethodOfGivingIDForCash { get; set; }

    public int? EMAEventIDForEtransfers { get; set; }
    public int? EMASourceIDForEtransfers { get; set; }
    public int? EMAMethodOfGivingIDForETransfers { get; set; }
    public int? EMAFundIDForEtransfer { get; set; }
    public int? EMACampusIDForEtransfers { get; set; }

    public int? EMAEventIDForPADs { get; set; }
    public int? EMASourceIDForPADs { get; set; }
    public int? EMAMethodOfGivingIDForPADs { get; set; }
    public int? EMAFundIDForPADs { get; set; }
    public int? EMACampusIDForPADs { get; set; }

    public int? EMAEventIDForCreditDebitCards { get; set; }
    public int? EMASourceIDForCreditDebitCards { get; set; }
    public int? EMAMethodOfGivingIDForCreditDebitCards { get; set; }
    public int? EMAFundIDForCreditDebitCards { get; set; }
    public int? EMACampusIDForCreditDebitCards { get; set; }
}

public sealed class OnboardingSaveResult
{
    public bool Succeeded { get; set; }
    public int StatusCode { get; set; }
    public string Message { get; set; } = string.Empty;
}
