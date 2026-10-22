using EMA.App.Models;

namespace EMA.App.Services;

public interface IOrganizationStateService
{
    OrganizationSummary? CurrentOrganization { get; }

    string? CurrentLogoBlobID { get; }

    string? CurrentLogoDataUrl { get; }

    event Action? OnChange;

    void UpdateOrganization(OrganizationSummary organization);

    void UpdateLogoDataUrl(string logoBlobID, string logoDataUrl);

    bool TryGetLogoDataUrl(string? logoBlobID, out string logoDataUrl);

    void ClearLogo();

    void Clear();
}