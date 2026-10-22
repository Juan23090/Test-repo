using EMA.App.Models;

namespace EMA.App.Services;

public interface IPublicOnboardingService
{
    Task<PublicOnboardingLookups?> GetLookupsAsync(
        CancellationToken cancellationToken = default);

    Task<PublicOnboardingSubmitResult> SubmitAsync(
        PublicOrganizationRequest request,
        CancellationToken cancellationToken = default);
}
