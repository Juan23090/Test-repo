using System.Net.Http.Json;
using EMA.App.Models;

namespace EMA.App.Services;

public sealed class SetupWizardService : ISetupWizardService
{
    private readonly HttpClient _http;

    public SetupWizardService(
        HttpClient http)
    {
        _http = http;
    }

    public async Task<SetupWizardResult?> GetForOrganizationUserAsync(
        int organizationID,
        CancellationToken cancellationToken = default)
    {
        if (organizationID <= 0)
        {
            return null;
        }

        try
        {
            return await _http.GetFromJsonAsync<SetupWizardResult>(
                $"api/organization/{organizationID}/setup-wizard",
                cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<SetupWizardResult?> GetForInsiderAsync(
        int organizationID,
        CancellationToken cancellationToken = default)
    {
        if (organizationID <= 0)
        {
            return null;
        }

        try
        {
            return await _http.GetFromJsonAsync<SetupWizardResult>(
                $"api/platform/setup-wizard/organizations/{organizationID}",
                cancellationToken);
        }
        catch
        {
            return null;
        }
    }
}
