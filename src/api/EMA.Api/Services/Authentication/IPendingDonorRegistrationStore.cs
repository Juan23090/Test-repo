namespace EMA.Api.Services.Authentication;

public interface IPendingDonorRegistrationStore
{
    Task<PendingDonorRegistrationRecord>
        CreateRegistrationAsync(
        int EMAOrganizationID,
        string officialDonorName,
        string? interacDonorName,
        string address,
        string city,
        string provinceStateCode,
        string postalCode,
        string country,
        string phoneNumber,
        string emailAddress,
        CancellationToken cancellationToken = default);

    Task<PendingDonorRegistrationRecord?>
        TryVerifyAndConsumeAsync(
        string registrationID,
        string code,
        CancellationToken cancellationToken = default);

    Task RevokeAsync(
        string registrationID,
        CancellationToken cancellationToken = default);
}
