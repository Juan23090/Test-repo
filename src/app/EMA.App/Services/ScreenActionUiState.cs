using EMA.App.Models;

namespace EMA.App.Services;

public sealed class ScreenActionUiState : IScreenActionUiState
{
    public event Action<ScreenEntryRequest>? EntryRequested;

    public void RequestEntry(
        ScreenEntryKind kind,
        int menuItemID)
    {
        if (menuItemID <= 0)
        {
            return;
        }

        EntryRequested?.Invoke(
            new ScreenEntryRequest(
                kind,
                menuItemID));
    }
}
