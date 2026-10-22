using EMA.App.Models;

namespace EMA.App.Services;

public class OrganizationStateService : IOrganizationStateService
{
    private OrganizationSummary? _currentOrganization;

    public OrganizationSummary? CurrentOrganization => _currentOrganization?.Clone();

    public string? CurrentLogoBlobID { get; private set; }

    public string? CurrentLogoDataUrl { get; private set; }

    public event Action? OnChange;

    public void UpdateOrganization(OrganizationSummary organization)
    {
        var previousLogoBlobID = _currentOrganization?.LogoBlobID;

        _currentOrganization = organization.Clone();

        if (!string.Equals(
                previousLogoBlobID,
                _currentOrganization.LogoBlobID,
                StringComparison.OrdinalIgnoreCase))
        {
            ClearLogoWithoutNotify();
        }

        OnChange?.Invoke();
    }

    public void UpdateLogoDataUrl(string logoBlobID, string logoDataUrl)
    {
        if (string.IsNullOrWhiteSpace(logoBlobID) || string.IsNullOrWhiteSpace(logoDataUrl))
        {
            ClearLogo();
            return;
        }

        CurrentLogoBlobID = logoBlobID.Trim();
        CurrentLogoDataUrl = logoDataUrl;

        OnChange?.Invoke();
    }

    public bool TryGetLogoDataUrl(string? logoBlobID, out string logoDataUrl)
    {
        if (!string.IsNullOrWhiteSpace(logoBlobID)
            && !string.IsNullOrWhiteSpace(CurrentLogoBlobID)
            && !string.IsNullOrWhiteSpace(CurrentLogoDataUrl)
            && string.Equals(logoBlobID.Trim(), CurrentLogoBlobID, StringComparison.OrdinalIgnoreCase))
        {
            logoDataUrl = CurrentLogoDataUrl;
            return true;
        }

        logoDataUrl = string.Empty;
        return false;
    }

    public void ClearLogo()
    {
        ClearLogoWithoutNotify();
        OnChange?.Invoke();
    }

    public void Clear()
    {
        _currentOrganization = null;
        ClearLogoWithoutNotify();
        OnChange?.Invoke();
    }

    private void ClearLogoWithoutNotify()
    {
        CurrentLogoBlobID = null;
        CurrentLogoDataUrl = null;
    }
}