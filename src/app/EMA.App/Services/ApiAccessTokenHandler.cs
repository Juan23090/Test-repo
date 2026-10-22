using System.Net.Http.Headers;
using Microsoft.AspNetCore.Components.WebAssembly.Http;
using Microsoft.Extensions.Configuration;

namespace EMA.App.Services;

public sealed class ApiAccessTokenHandler :
    DelegatingHandler
{
    private const string
        SwaAccessTokenHeaderName =
            "X-EMA-Access-Token";

    private const string
        StaticWebAppsHostSuffix =
            ".azurestaticapps.net";

    private readonly IUserSessionStateService
        _sessionState;

    private readonly bool
        _useSwaManagedAuthCompatibility;

    public ApiAccessTokenHandler(
        IUserSessionStateService sessionState,
        IConfiguration configuration)
    {
        _sessionState =
            sessionState;

        /*
         * Azure Static Web Apps managed Functions use the
         * Authorization header for their own authentication
         * transport.
         *
         * Our test environment currently uses SWA managed
         * Functions, while local development and production
         * do not.
         *
         * Therefore:
         *
         *   Local / Production:
         *       Authorization: Bearer <EMA token>
         *
         *   Test SWA:
         *       X-EMA-Access-Token: <EMA token>
         *
         * This is determined from the already environment-
         * specific ApiBaseUrl. No secret or security decision
         * is based on client-controlled page data.
         *
         * The API independently requires
         * USE_SWA_MANAGED_AUTH_COMPATIBILITY=true before it
         * will accept the custom header, so the server remains
         * fail-closed if this client-side detection is wrong.
         */
        var apiBaseUrl =
            configuration["ApiBaseUrl"];

        _useSwaManagedAuthCompatibility =
            Uri.TryCreate(
                apiBaseUrl,
                UriKind.Absolute,
                out var apiUri) &&
            IsStaticWebAppsHost(
                apiUri.Host);
    }

    protected override async Task<
        HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        await _sessionState.InitializeAsync();

        var session =
            _sessionState.CurrentSession;

        if (session is not null &&
            session.AccessTokenExpiresAtUtc
                .HasValue &&
            session.AccessTokenExpiresAtUtc
                .Value <= DateTimeOffset.UtcNow)
        {
            await _sessionState
                .ClearLocalSessionAsync(
                    sessionExpired: true);

            session = null;
        }

        var emaAccessTokenAttached =
            false;

        if (session is not null &&
            !string.IsNullOrWhiteSpace(
                session.AccessToken) &&
            session.AccessTokenExpiresAtUtc >
                DateTimeOffset.UtcNow)
        {
            if (_useSwaManagedAuthCompatibility)
            {
                /*
                 * Do not also send the EMA token through
                 * Authorization in SWA compatibility mode.
                 *
                 * SWA's managed authentication layer owns that
                 * header before the request reaches the Function
                 * worker.
                 */
                request.Headers.Remove(
                    SwaAccessTokenHeaderName);

                if (!request.Headers
                        .TryAddWithoutValidation(
                            SwaAccessTokenHeaderName,
                            session.AccessToken))
                {
                    throw new InvalidOperationException(
                        "Unable to attach the EMA access token header.");
                }

                emaAccessTokenAttached =
                    true;
            }
            else
            {
                /*
                 * Standard bearer-token transport.
                 *
                 * This remains the authentication mechanism
                 * used by local development and production.
                 */
                if (request.Headers.Authorization is null)
                {
                    request.Headers.Authorization =
                        new AuthenticationHeaderValue(
                            "Bearer",
                            session.AccessToken);
                }

                emaAccessTokenAttached =
                    request.Headers.Authorization
                        is not null;
            }
        }

        request.SetBrowserRequestCredentials(
            BrowserRequestCredentials.Include);

        var response =
            await base.SendAsync(
                request,
                cancellationToken);

        if (response.StatusCode ==
                System.Net.HttpStatusCode.Unauthorized &&
            emaAccessTokenAttached)
        {
            await _sessionState
                .ClearLocalSessionAsync(
                    sessionExpired: true);
        }

        return response;
    }

    private static bool IsStaticWebAppsHost(
        string host)
    {
        if (string.IsNullOrWhiteSpace(
                host))
        {
            return false;
        }

        return host.Equals(
                   "azurestaticapps.net",
                   StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(
                   StaticWebAppsHostSuffix,
                   StringComparison.OrdinalIgnoreCase);
    }
}