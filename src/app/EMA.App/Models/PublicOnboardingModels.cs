namespace EMA.App.Models;

public sealed class PublicOnboardingLookups
{
    public List<PublicChMSOption> ChMSOptions { get; set; } = [];
    public List<PublicAccountingSoftwareOption> AccountingSoftwareOptions { get; set; } = [];
}

public sealed class PublicChMSOption
{
    public int ChMSID { get; set; }
    public string ChMSName { get; set; } = string.Empty;
}

public sealed class PublicAccountingSoftwareOption
{
    public int AccountingSoftwareID { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class PublicOrganizationRequest
{
    public string? OrganizationName { get; set; }
    public string? AdminName { get; set; }
    public string? AdminEmail { get; set; }
    public string? AdminEmailVerificationToken { get; set; }
    public string? AdminPhone { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? ProvinceStateCode { get; set; }
    public string? PostalCode { get; set; }
    public string? AnnualBudget { get; set; }
    public int? ChMSID { get; set; }
    public int? AccountingSoftwareID { get; set; }
    public int? OrganizationStatusID { get; set; }
}

public sealed class PublicOrganizationRequestCreated
{
    public int EMAOrganizationID { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class PublicOnboardingSubmitResult
{
    public bool Succeeded { get; set; }
    public int StatusCode { get; set; }
    public int? EMAOrganizationID { get; set; }
    public string Message { get; set; } = string.Empty;
}
