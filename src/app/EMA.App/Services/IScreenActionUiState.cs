using EMA.App.Models;

namespace EMA.App.Services;

public interface IScreenActionUiState
{
    event Action<ScreenEntryRequest>? EntryRequested;

    void RequestEntry(
        ScreenEntryKind kind,
        int menuItemID);
}
