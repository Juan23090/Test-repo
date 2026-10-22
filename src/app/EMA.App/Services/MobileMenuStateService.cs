namespace EMA.App.Services;

public sealed class MobileMenuStateService :
    IMobileMenuStateService
{
    public bool IsExpanded
    {
        get;
        private set;
    }

    public event Action? OnChange;

    public void Expand()
    {
        SetExpanded(true);
    }

    public void Collapse()
    {
        SetExpanded(false);
    }

    public void Toggle()
    {
        SetExpanded(!IsExpanded);
    }

    private void SetExpanded(bool expanded)
    {
        if (IsExpanded == expanded)
        {
            return;
        }

        IsExpanded = expanded;
        OnChange?.Invoke();
    }
}
