using EMA.App.Models;

namespace EMA.App.Services;

public interface IOrganizationOnboardingService
{
    Task<OrganizationOnboardingSettings?> GetAsync(
        int organizationID,
        CancellationToken cancellationToken = default);

    Task<OnboardingSaveResult> SaveAsync(
        int organizationID,
        string section,
        SaveOrganizationOnboardingSettingsRequest request,
        CancellationToken cancellationToken = default);
}
