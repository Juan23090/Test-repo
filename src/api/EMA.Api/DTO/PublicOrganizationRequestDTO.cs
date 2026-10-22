namespace EMA.Api.DTOs;

public sealed class PublicOrganizationRequestDto
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

public sealed class PublicOrganizationRequestCreatedDto
{
    public int EMAOrganizationID { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class PublicOnboardingLookupsDto
{
    public List<PublicChMSOptionDto> ChMSOptions { get; set; } = [];
    public List<PublicAccountingSoftwareOptionDto> AccountingSoftwareOptions { get; set; } = [];
}

public sealed class PublicChMSOptionDto
{
    public int ChMSID { get; set; }
    public string ChMSName { get; set; } = string.Empty;
}

public sealed class PublicAccountingSoftwareOptionDto
{
    public int AccountingSoftwareID { get; set; }
    public string Name { get; set; } = string.Empty;
}
