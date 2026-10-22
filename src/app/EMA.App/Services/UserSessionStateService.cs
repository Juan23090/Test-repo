using System.Text.Json;
using EMA.App.Models;
using Microsoft.JSInterop;

namespace EMA.App.Services;

public sealed class UserSessionStateService :
    IUserSessionStateService
{
    private const string StorageKey =
        "ema-user-session";

    private readonly IJSRuntime _jsRuntime;
    private readonly IHttpClientFactory
        _httpClientFactory;

    private readonly JsonSerializerOptions
        _jsonOptions =
            new(JsonSerializerDefaults.Web);

    private Task? _initializationTask;

    public UserSessionStateService(
        IJSRuntime jsRuntime,
        IHttpClientFactory httpClientFactory)
    {
        _jsRuntime =
            jsRuntime;

        _httpClientFactory =
            httpClientFactory;
    }

    public UserSession? CurrentSession
    {
        get;
        private set;
    }

    public bool IsLoggedIn =>
        CurrentSession is not null;

    public bool IsInitialized
    {
        get;
        private set;
    }

    public bool SessionExpired
    {
        get;
        private set;
    }

    public event Action? OnChange;

    public Task InitializeAsync()
    {
        _initializationTask ??=
            InitializeCoreAsync();

        return _initializationTask;
    }

    public async Task SignInAsync(
        LoginResponse loginResponse)
    {
        SessionExpired = false;

        ApplyLoginResponse(
            loginResponse);

        await PersistCurrentSessionSafeAsync();

        NotifyStateChanged();
    }

    public void SignIn(
        LoginResponse loginResponse)
    {
        SessionExpired = false;

        ApplyLoginResponse(
            loginResponse);

        NotifyStateChanged();

        _ =
            PersistCurrentSessionSafeAsync();
    }

    public async Task<bool>
        TrySwitchCategoryAsync(
            int categoryID)
    {
        if (!SetCurrentCategory(
                categoryID))
        {
            return false;
        }

        await PersistCurrentSessionSafeAsync();

        NotifyStateChanged();

        return true;
    }

    public bool TrySwitchCategory(
        int categoryID)
    {
        if (!SetCurrentCategory(
                categoryID))
        {
            return false;
        }

        NotifyStateChanged();

        _ =
            PersistCurrentSessionSafeAsync();

        return true;
    }

    public async Task SignOutAsync()
    {
        SessionExpired = false;

        if (CurrentSession is not null)
        {
            try
            {
                var client =
                    _httpClientFactory
                        .CreateClient(
                            "Api");

                await client.PostAsync(
                    "/api/auth/logout",
                    content: null);
            }
            catch
            {
                // Local state must still be cleared when the API is
                // unavailable. Access tokens also have a short expiry.
            }
        }

        CurrentSession = null;

        await RemoveStoredSessionSafeAsync();

        NotifyStateChanged();
    }

    public async Task ClearLocalSessionAsync(
        bool sessionExpired = false)
    {
        SessionExpired = sessionExpired;
        CurrentSession = null;

        await RemoveStoredSessionSafeAsync();

        NotifyStateChanged();
    }

    public void SignOut()
    {
        SessionExpired = false;
        CurrentSession = null;

        NotifyStateChanged();

        _ =
            RemoveStoredSessionSafeAsync();
    }

    private async Task InitializeCoreAsync()
    {
        try
        {
            var json =
                await _jsRuntime
                    .InvokeAsync<string?>(
                        "sessionStorage.getItem",
                        StorageKey);

            if (string.IsNullOrWhiteSpace(
                    json))
            {
                CurrentSession = null;

                return;
            }

            var storedSession =
                JsonSerializer
                    .Deserialize<UserSession>(
                        json,
                        _jsonOptions);

            if (IsStoredSessionValid(
                    storedSession))
            {
                SessionExpired = false;
                CurrentSession =
                    storedSession;
            }
            else
            {
                SessionExpired =
                    HasExpiredAccessToken(
                        storedSession);

                CurrentSession = null;

                await RemoveStoredSessionSafeAsync();
            }
        }
        catch
        {
            CurrentSession = null;
        }
        finally
        {
            IsInitialized = true;

            NotifyStateChanged();
        }
    }

    private void ApplyLoginResponse(
        LoginResponse loginResponse)
    {
        ArgumentNullException.ThrowIfNull(
            loginResponse);

        var allowedCategoryIDs =
            loginResponse
                .AllowedCategoryIDs
                .Distinct()
                .ToList();

        var currentCategoryID =
            allowedCategoryIDs.Contains(
                loginResponse.DefaultCategoryID)
                ? loginResponse.DefaultCategoryID
                : allowedCategoryIDs.FirstOrDefault();

        CurrentSession =
            new UserSession
            {
                UserID =
                    loginResponse.UserID,

                DonorID =
                    loginResponse.DonorID,

                IsDonorSession =
                    loginResponse.IsDonorSession,

                UserName =
                    loginResponse.UserName,

                EmailAddress =
                    loginResponse.EmailAddress,

                PhoneNumber =
                    loginResponse.PhoneNumber,

                EMAOrganizationID =
                    loginResponse.EMAOrganizationID,

                OrganizationName =
                    loginResponse.OrganizationName,

                Roles =
                    loginResponse.Roles.ToList(),

                AllowedCategoryIDs =
                    allowedCategoryIDs,

                CurrentCategoryID =
                    currentCategoryID,

                AccessToken =
                    loginResponse.AccessToken,

                AccessTokenExpiresAtUtc =
                    loginResponse
                        .AccessTokenExpiresAtUtc
            };

        IsInitialized = true;

        _initializationTask =
            Task.CompletedTask;
    }

    private bool SetCurrentCategory(
        int categoryID)
    {
        if (CurrentSession is null)
        {
            return false;
        }

        if (!CurrentSession
                .AllowedCategoryIDs
                .Contains(
                    categoryID))
        {
            return false;
        }

        CurrentSession.CurrentCategoryID =
            categoryID;

        return true;
    }

    private static bool HasExpiredAccessToken(
        UserSession? session)
    {
        return session?.AccessTokenExpiresAtUtc
                   is { } expiresAt &&
               expiresAt <= DateTimeOffset.UtcNow;
    }

    private static bool IsStoredSessionValid(
        UserSession? session)
    {
        if (session is null ||
            session.EMAOrganizationID <= 0 ||
            string.IsNullOrWhiteSpace(
                session.AccessToken) ||
            !session.AccessTokenExpiresAtUtc
                .HasValue ||
            session.AccessTokenExpiresAtUtc
                .Value <= DateTimeOffset.UtcNow)
        {
            return false;
        }

        if (session.IsDonorSession)
        {
            if (!session.DonorID.HasValue ||
                session.DonorID.Value <= 0)
            {
                return false;
            }
        }
        else if (session.UserID <= 0)
        {
            return false;
        }

        if (session.AllowedCategoryIDs
            is not { Count: > 0 })
        {
            return false;
        }

        return session
            .AllowedCategoryIDs
            .Contains(
                session.CurrentCategoryID);
    }

    private async Task
        PersistCurrentSessionSafeAsync()
    {
        try
        {
            if (CurrentSession is null)
            {
                await RemoveStoredSessionSafeAsync();

                return;
            }

            var json =
                JsonSerializer.Serialize(
                    CurrentSession,
                    _jsonOptions);

            await _jsRuntime.InvokeVoidAsync(
                "sessionStorage.setItem",
                StorageKey,
                json);
        }
        catch
        {
            // Browser storage failure must not
            // prevent the active login from working.
        }
    }

    private async Task
        RemoveStoredSessionSafeAsync()
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync(
                "sessionStorage.removeItem",
                StorageKey);
        }
        catch
        {
            // Do not crash logout or initialization
            // when browser storage is unavailable.
        }
    }

    private void NotifyStateChanged()
    {
        OnChange?.Invoke();
    }
}
