using EMA.App.Models;

namespace EMA.App.Services;

public interface ILoginService
{
    Task<LoginServiceResult<List<PublicOrganizationLookup>>> SearchPublicOrganizationsAsync(
        string term,
        CancellationToken cancellationToken = default);

    Task<LoginServiceResult<DonorOrganizationLoginLookupResponse>> LookupDonorForOrganizationAsync(
        int EMAOrganizationID,
        string phoneNumber,
        CancellationToken cancellationToken = default);

    Task<LoginServiceResult<StartDonorSelfRegistrationVerificationResponse>> StartDonorSelfRegistrationVerificationAsync(
        StartDonorSelfRegistrationVerificationRequest request,
        CancellationToken cancellationToken = default);

    Task<LoginServiceResult<LoginResponse>> VerifyDonorSelfRegistrationAsync(
        string registrationID,
        string code,
        CancellationToken cancellationToken = default);

    Task<LoginServiceResult<LoginVerificationStartResponse>> LookupUnifiedLoginAsync(
        string phoneNumber,
        string emailAddress,
        CancellationToken cancellationToken = default);

    Task<LoginServiceResult<UnifiedLoginLookupResponse>> VerifyUnifiedLoginCodeAsync(
        string phoneNumber,
        string emailAddress,
        string verificationID,
        string code,
        CancellationToken cancellationToken = default);

    Task<LoginServiceResult<LoginResponse>> CompleteUnifiedLoginAsync(
        string phoneNumber,
        string emailAddress,
        string verificationID,
        string actorType,
        int EMAOrganizationID,
        int? userID,
        int? donorID,
        CancellationToken cancellationToken = default);

    Task<LoginServiceResult<LoginVerificationStartResponse>> StartOrganizationLoginVerificationAsync(
        int EMAOrganizationID,
        int userID,
        CancellationToken cancellationToken = default);

    Task<LoginServiceResult<LoginVerificationStartResponse>> StartDonorLoginVerificationAsync(
        int EMAOrganizationID,
        int donorID,
        CancellationToken cancellationToken = default);

    Task<LoginServiceResult<LoginLookupResponse>> LookupUserAsync(
        string identifier,
        CancellationToken cancellationToken = default);

    Task<LoginServiceResult<LoginResponse>> LoginAsync(
        int EMAOrganizationID,
        int userID,
        CancellationToken cancellationToken = default);

    Task<LoginServiceResult<DonorLoginLookupResponse>> LookupDonorAsync(
        string identifier,
        CancellationToken cancellationToken = default);

    Task<LoginServiceResult<LoginResponse>> DonorLoginAsync(
        int EMAOrganizationID,
        int donorID,
        CancellationToken cancellationToken = default);

    Task<LoginServiceResult<LoginResponse>> VerifyOrganizationLoginCodeAsync(
        int EMAOrganizationID,
        int userID,
        string verificationID,
        string code,
        CancellationToken cancellationToken = default);

    Task<LoginServiceResult<LoginResponse>> VerifyDonorLoginCodeAsync(
        int EMAOrganizationID,
        int donorID,
        string verificationID,
        string code,
        CancellationToken cancellationToken = default);
}
