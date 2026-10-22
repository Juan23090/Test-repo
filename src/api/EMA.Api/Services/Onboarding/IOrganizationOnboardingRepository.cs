using EMA.Api.DTOs;

namespace EMA.Api.Services.Onboarding;

public interface IOrganizationOnboardingRepository
{
    Task<OrganizationOnboardingSettingsDto?> GetAsync(
        int organizationID,
        CancellationToken cancellationToken = default);

    Task<OrganizationOnboardingSaveResult> SaveAsync(
        int organizationID,
        int actorUserID,
        string section,
        SaveOrganizationOnboardingSettingsRequestDto request,
        CancellationToken cancellationToken = default);
}
