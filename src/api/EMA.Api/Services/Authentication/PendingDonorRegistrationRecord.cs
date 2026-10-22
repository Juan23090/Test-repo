namespace EMA.Api.Services.Authentication;

public sealed class PendingDonorRegistrationRecord
{
    public string RegistrationID { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

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

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public int FailedAttempts { get; set; }
}