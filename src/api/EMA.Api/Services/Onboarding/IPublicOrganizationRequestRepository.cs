using EMA.Api.DTOs;

namespace EMA.Api.Services.Onboarding;

public interface IPublicOrganizationRequestRepository
{
    Task<PublicOnboardingLookupsDto> GetLookupsAsync(
        CancellationToken cancellationToken = default);

    Task<int> CreateAsync(
        PublicOrganizationRequestDto request,
        CancellationToken cancellationToken = default);
}
