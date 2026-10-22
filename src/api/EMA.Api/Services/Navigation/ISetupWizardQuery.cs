using EMA.Api.DTOs;

namespace EMA.Api.Services.Navigation;

public interface ISetupWizardQuery
{
    Task<SetupWizardResponseDto?> GetForOrganizationUserAsync(
        int organizationID,
        int userID,
        bool includeAllWizardSteps,
        CancellationToken cancellationToken = default);

    Task<SetupWizardResponseDto?> GetForPlatformAdministratorAsync(
        int organizationID,
        CancellationToken cancellationToken = default);
}
