namespace EMA.App.Services;

public interface IMobileMenuStateService
{
    bool IsExpanded { get; }

    event Action? OnChange;

    void Expand();

    void Collapse();

    void Toggle();
}
