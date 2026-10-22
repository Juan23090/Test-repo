using EMA.App.Models;

namespace EMA.App.Services;

public interface ISetupWizardService
{
    Task<SetupWizardResult?> GetForOrganizationUserAsync(
        int organizationID,
        CancellationToken cancellationToken = default);

    Task<SetupWizardResult?> GetForInsiderAsync(
        int organizationID,
        CancellationToken cancellationToken = default);
}
