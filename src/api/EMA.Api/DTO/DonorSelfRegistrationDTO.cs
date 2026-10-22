namespace EMA.Api.DTOs;

public sealed class PublicOrganizationLookupDto
{
    public int EMAOrganizationID { get; set; }

    public string OrganizationName { get; set; } = string.Empty;
}

public sealed class DonorOrganizationLoginLookupRequestDto
{
    public string Identifier { get; set; } = string.Empty;

    // Kept for compatibility with older clients.
    public string PhoneNumber { get; set; } = string.Empty;
}

public sealed class DonorOrganizationLoginLookupResponseDto
{
    public int EMAOrganizationID { get; set; }

    public string OrganizationName { get; set; } = string.Empty;

    public int DonorID { get; set; }

    public string DonorName { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;
}

public sealed class StartDonorSelfRegistrationVerificationRequestDto
{
    public int EMAOrganizationID { get; set; }

    public string OfficialDonorName { get; set; } = string.Empty;

    public string? InteracDonorName { get; set; }

    public string Address { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    public string ProvinceStateCode { get; set; } = string.Empty;

    public string PostalCode { get; set; } = string.Empty;

    public string Country { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    public string EmailAddress { get; set; } = string.Empty;
}

public sealed class StartDonorSelfRegistrationVerificationResponseDto
{
    public string RegistrationID { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}

public sealed class VerifyDonorSelfRegistrationRequestDto
{
    public string RegistrationID { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;
}