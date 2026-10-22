using EMA.App.Models;

namespace EMA.App.Services;

public interface IUserSessionStateService
{
    UserSession? CurrentSession { get; }

    bool IsLoggedIn { get; }

    bool IsInitialized { get; }

    bool SessionExpired { get; }

    event Action? OnChange;

    Task InitializeAsync();

    Task SignInAsync(
        LoginResponse loginResponse);

    Task<bool> TrySwitchCategoryAsync(
        int categoryID);

    Task SignOutAsync();

    Task ClearLocalSessionAsync(
        bool sessionExpired = false);

    // Compatibility methods for any existing callers.
    void SignIn(
        LoginResponse loginResponse);

    bool TrySwitchCategory(
        int categoryID);

    void SignOut();
}
