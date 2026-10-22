namespace EMA.Api.Services.Authentication;

public interface IDonorAccessTokenService
{
    DonorAccessToken CreateDonorSessionToken(
        int donorID,
        int EMAOrganizationID);

    Task<string> CreateGivingHistoryUrlAsync(
        int donorID,
        CancellationToken cancellationToken = default);

    string CreateDonationChangesUrl(
        long donationID,
        int donorID,
        int EMAOrganizationID);

    string GetDonorLoginUrl();

    bool TryValidateDonorSessionToken(
        string token,
        out DonorAccessIdentity? identity);

    bool TryValidateGivingHistoryToken(
        string token,
        out DonorAccessIdentity? identity);

    bool TryValidateDonationChangesToken(
        string token,
        long donationID,
        out DonorAccessIdentity? identity);
}

public sealed record DonorAccessToken(
    string Token,
    DateTimeOffset ExpiresAtUtc);

public sealed record DonorAccessIdentity(
    int DonorID,
    int EMAOrganizationID);
