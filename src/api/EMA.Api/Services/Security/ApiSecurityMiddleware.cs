using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace EMA.Api.Services.Security;

public sealed class ApiSecurityMiddleware :
    IFunctionsWorkerMiddleware
{
    private const string
        SwaAccessTokenHeaderName =
            "X-EMA-Access-Token";

    private readonly ApiEndpointPolicyRegistry
        _policyRegistry;

    private readonly IApiAccessTokenService
        _accessTokenService;

    private readonly IApiSessionService
        _sessionService;

    private readonly ApiRequestBoundaryValidator
        _boundaryValidator;

    private readonly ApiCorsPolicy
        _corsPolicy;

    private readonly IApiRequestRateLimiter
        _rateLimiter;

    private readonly ILogger<ApiSecurityMiddleware>
        _logger;

    private readonly bool
        _authDiagnosticsEnabled;

    private readonly bool
        _swaManagedAuthCompatibilityEnabled;

    public ApiSecurityMiddleware(
        ApiEndpointPolicyRegistry policyRegistry,
        IApiAccessTokenService accessTokenService,
        IApiSessionService sessionService,
        ApiRequestBoundaryValidator boundaryValidator,
        ApiCorsPolicy corsPolicy,
        IApiRequestRateLimiter rateLimiter,
        ILogger<ApiSecurityMiddleware> logger)
    {
        _policyRegistry =
            policyRegistry;

        _accessTokenService =
            accessTokenService;

        _sessionService =
            sessionService;

        _boundaryValidator =
            boundaryValidator;

        _corsPolicy =
            corsPolicy;

        _rateLimiter =
            rateLimiter;

        _logger =
            logger;

        /*
         * Temporary, opt-in diagnostics.
         *
         * No token contents, signatures, payloads, or token
         * fragments are ever logged.
         */
        _authDiagnosticsEnabled =
            string.Equals(
                Environment.GetEnvironmentVariable(
                    "ENABLE_AUTH_DIAGNOSTICS"),
                "true",
                StringComparison.OrdinalIgnoreCase);

        /*
         * Azure Static Web Apps managed Functions use the
         * Authorization header as part of their managed
         * authentication transport.
         *
         * The explicit flag remains supported, but deployed SWA
         * requests are also recognized from their request host.
         * This prevents a client/server transport mismatch when
         * the managed API runtime does not expose a separately
         * configured application setting.
         *
         * Non-SWA requests still accept only the standards-based
         * Authorization: Bearer transport unless the explicit
         * compatibility flag is enabled.
         */
        _swaManagedAuthCompatibilityEnabled =
            string.Equals(
                Environment.GetEnvironmentVariable(
                    "USE_SWA_MANAGED_AUTH_COMPATIBILITY"),
                "true",
                StringComparison.OrdinalIgnoreCase);
    }

    public async Task Invoke(
        FunctionContext context,
        FunctionExecutionDelegate next)
    {
        var httpContext =
            context.GetHttpContext();

        if (httpContext is null)
        {
            await next(
                context);

            return;
        }

        if (!_corsPolicy
                .IsRequestOriginAllowed(
                    httpContext.Request))
        {
            await RejectAsync(
                context,
                httpContext,
                StatusCodes.Status403Forbidden,
                "The request origin is not allowed.");

            return;
        }

        _corsPolicy.ApplyResponseHeaders(
            httpContext.Request,
            httpContext.Response);

        if (HttpMethods.IsOptions(
                httpContext.Request.Method))
        {
            if (!_corsPolicy.IsValidPreflight(
                    httpContext.Request))
            {
                await RejectAsync(
                    context,
                    httpContext,
                    StatusCodes.Status400BadRequest,
                    "The CORS preflight request is invalid.");

                return;
            }

            await CompletePreflightAsync(
                context);

            return;
        }

        var functionName =
            context.FunctionDefinition.Name;

        var policy =
            _policyRegistry.GetPolicy(
                functionName);

        if (httpContext.Request.ContentLength >
            policy.MaximumRequestBodyBytes)
        {
            await RejectAsync(
                context,
                httpContext,
                StatusCodes
                    .Status413PayloadTooLarge,
                "The request body is too large.");

            return;
        }

        if (policy.Access ==
            ApiEndpointAccess.Disabled)
        {
            await RejectAsync(
                context,
                httpContext,
                StatusCodes.Status404NotFound,
                "The requested endpoint is not available.");

            return;
        }

        if (policy.Access ==
            ApiEndpointAccess.Public)
        {
            if (!await ApplyRateLimitAsync(
                    context,
                    httpContext,
                    functionName,
                    identity: null))
            {
                return;
            }

            await ExecuteNextAsync(
                context,
                httpContext,
                next);

            return;
        }

        /*
         * Capture only non-sensitive characteristics of both
         * possible authentication transports.
         *
         * Never log either header value.
         */
        var authorizationHeader =
            httpContext.Request
                .Headers.Authorization
                .ToString();

        var authorizationHeaderPresent =
            !string.IsNullOrWhiteSpace(
                authorizationHeader);

        var bearerSchemePresent =
            authorizationHeader.StartsWith(
                "Bearer ",
                StringComparison.OrdinalIgnoreCase);

        var authorizationTokenCandidate =
            bearerSchemePresent
                ? authorizationHeader[
                    "Bearer ".Length..]
                    .Trim()
                : string.Empty;

        var authorizationEmaPrefixPresent =
            authorizationTokenCandidate
                .StartsWith(
                    "ema1.",
                    StringComparison.Ordinal);

        var swaHeaderValue =
            httpContext.Request.Headers[
                    SwaAccessTokenHeaderName]
                .ToString();

        var swaHeaderPresent =
            !string.IsNullOrWhiteSpace(
                swaHeaderValue);

        var swaEmaPrefixPresent =
            swaHeaderValue
                .Trim()
                .StartsWith(
                    "ema1.",
                    StringComparison.Ordinal);

        var effectiveSwaCompatibilityEnabled =
            ShouldUseSwaManagedAuthCompatibility(
                httpContext.Request,
                _swaManagedAuthCompatibilityEnabled);

        var accessTokenPresent =
            TryGetAccessToken(
                httpContext.Request,
                effectiveSwaCompatibilityEnabled,
                out var accessToken,
                out var accessTokenTransport);

        ApiRequestIdentity? identity =
            null;

        var tokenValidated =
            accessTokenPresent &&
            _accessTokenService
                .TryValidateToken(
                    accessToken,
                    out identity) &&
            identity is not null;

        if (_authDiagnosticsEnabled)
        {
            _logger.LogWarning(
                "API authentication diagnostics for {FunctionName}: " +
                "SwaCompatibilityEnabled={SwaCompatibilityEnabled}, " +
                "AuthorizationHeaderPresent={AuthorizationHeaderPresent}, " +
                "BearerSchemePresent={BearerSchemePresent}, " +
                "AuthorizationEmaPrefixPresent={AuthorizationEmaPrefixPresent}, " +
                "SwaHeaderPresent={SwaHeaderPresent}, " +
                "SwaEmaPrefixPresent={SwaEmaPrefixPresent}, " +
                "SelectedTransport={SelectedTransport}, " +
                "TokenLength={TokenLength}, " +
                "TokenValidated={TokenValidated}.",
                functionName,
                effectiveSwaCompatibilityEnabled,
                authorizationHeaderPresent,
                bearerSchemePresent,
                authorizationEmaPrefixPresent,
                swaHeaderPresent,
                swaEmaPrefixPresent,
                accessTokenTransport,
                accessToken.Length,
                tokenValidated);
        }

        if (!tokenValidated ||
            identity is null)
        {
            if (policy.Access ==
                ApiEndpointAccess
                    .SelfContainedDonorToken)
            {
                if (!await ApplyRateLimitAsync(
                        context,
                        httpContext,
                        functionName,
                        identity: null))
                {
                    return;
                }

                await ExecuteNextAsync(
                    context,
                    httpContext,
                    next);

                return;
            }

            await RejectAsync(
                context,
                httpContext,
                StatusCodes.Status401Unauthorized,
                "A valid access token is required.");

            return;
        }

        var sessionActive =
            await _sessionService
                .IsSessionActiveAsync(
                    identity,
                    httpContext.RequestAborted);

        if (_authDiagnosticsEnabled)
        {
            _logger.LogWarning(
                "API session diagnostics for {FunctionName}: " +
                "TokenValidated={TokenValidated}, " +
                "SessionActive={SessionActive}.",
                functionName,
                true,
                sessionActive);
        }

        if (!sessionActive)
        {
            await RejectAsync(
                context,
                httpContext,
                StatusCodes.Status401Unauthorized,
                "The session is expired or no longer active.");

            return;
        }

        if (!await ApplyRateLimitAsync(
                context,
                httpContext,
                functionName,
                identity))
        {
            return;
        }

        if (!IsActorAllowed(
                policy,
                identity))
        {
            await RejectAsync(
                context,
                httpContext,
                StatusCodes.Status403Forbidden,
                "This session is not permitted to use the requested endpoint.");

            return;
        }

        var boundaryResult =
            await _boundaryValidator
                .ValidateAsync(
                    context,
                    httpContext,
                    identity,
                    policy);

        if (!boundaryResult.IsAllowed)
        {
            _logger.LogWarning(
                "Tenant boundary rejected request to {FunctionName} for session {SessionID}.",
                functionName,
                identity.SessionID);

            await RejectAsync(
                context,
                httpContext,
                boundaryResult.StatusCode,
                boundaryResult.Message);

            return;
        }

        httpContext.User =
            identity.ToClaimsPrincipal();

        context.Items[
            typeof(ApiRequestIdentity)] =
                identity;

        await ExecuteNextAsync(
            context,
            httpContext,
            next);
    }

    private async Task ExecuteNextAsync(
        FunctionContext context,
        HttpContext httpContext,
        FunctionExecutionDelegate next)
    {
        await next(
            context);

        if (context
                .GetInvocationResult()
                .Value is HttpResponseData
                responseData)
        {
            _corsPolicy.ApplyResponseHeaders(
                httpContext.Request,
                responseData);
        }

        _corsPolicy.ApplyResponseHeaders(
            httpContext.Request,
            httpContext.Response);
    }

    private bool IsActorAllowed(
        ApiEndpointPolicy policy,
        ApiRequestIdentity identity)
    {
        return policy.Access
            switch
        {
            ApiEndpointAccess
                    .OrganizationUser =>
                identity.ActorType ==
                ApiActorType.OrganizationUser,

            ApiEndpointAccess.Donor =>
                identity.ActorType ==
                ApiActorType.Donor,

            ApiEndpointAccess
                    .SelfContainedDonorToken =>
                identity.ActorType ==
                ApiActorType.Donor,

            ApiEndpointAccess.Authenticated =>
                identity.ActorType is
                    ApiActorType
                        .OrganizationUser or
                    ApiActorType.Donor,

            ApiEndpointAccess
                    .PlatformAdministrator =>
                _boundaryValidator
                    .IsPlatformAdministrator(
                        identity),

            ApiEndpointAccess
                    .OrganizationAdministrator =>
                _boundaryValidator
                    .IsOrganizationAdministrator(
                        identity),

            _ => false
        };
    }

    private static bool TryGetAccessToken(
        HttpRequest request,
        bool useSwaManagedAuthCompatibility,
        out string token,
        out string transport)
    {
        /*
         * Fail closed:
         *
         * When SWA compatibility is enabled, only the dedicated
         * EMA header is accepted.
         *
         * When it is disabled, only the standards-based
         * Authorization: Bearer transport is accepted.
         *
         * Non-SWA requests therefore never silently start accepting
         * the SWA compatibility header.
         */
        if (useSwaManagedAuthCompatibility)
        {
            transport =
                SwaAccessTokenHeaderName;

            token =
                request.Headers[
                        SwaAccessTokenHeaderName]
                    .ToString()
                    .Trim();

            return !string.IsNullOrWhiteSpace(
                       token) &&
                   token.Length <= 8192;
        }

        transport =
            "Authorization";

        return TryGetBearerToken(
            request,
            out token);
    }

    internal static bool ShouldUseSwaManagedAuthCompatibility(
        HttpRequest request,
        bool configuredCompatibilityEnabled)
    {
        if (configuredCompatibilityEnabled)
        {
            return true;
        }

        /*
         * Only the normalized request host is used for automatic
         * Static Web Apps detection. Do not inspect raw forwarded
         * host headers here: a direct client can supply those
         * headers and must not be able to select an alternate
         * authentication transport.
         *
         * Deployments whose trusted proxy setup rewrites Request.Host
         * to an internal hostname can opt in explicitly with
         * USE_SWA_MANAGED_AUTH_COMPATIBILITY=true.
         */
        return IsStaticWebAppsHost(
            request.Host.Host);
    }

    internal static bool IsStaticWebAppsHost(
        string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        var normalizedHost =
            host.Trim().TrimEnd('.');

        return normalizedHost.Equals(
                   "azurestaticapps.net",
                   StringComparison.OrdinalIgnoreCase) ||
               normalizedHost.EndsWith(
                   ".azurestaticapps.net",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetBearerToken(
        HttpRequest request,
        out string token)
    {
        token =
            string.Empty;

        var headerValue =
            request.Headers.Authorization
                .ToString();

        if (string.IsNullOrWhiteSpace(
                headerValue) ||
            !headerValue.StartsWith(
                "Bearer ",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        token =
            headerValue[
                "Bearer ".Length..]
                .Trim();

        return !string.IsNullOrWhiteSpace(
                   token) &&
               token.Length <= 8192;
    }

    private static Task RejectAsync(
        FunctionContext functionContext,
        HttpContext httpContext,
        int statusCode,
        string message)
    {
        if (!httpContext.Response.HasStarted)
        {
            httpContext.Response.Headers[
                HeaderNames.CacheControl] =
                "no-store";
        }

        functionContext
            .GetInvocationResult()
            .Value =
                new ObjectResult(
                    new
                    {
                        message
                    })
                {
                    StatusCode =
                        statusCode
                };

        return Task.CompletedTask;
    }

    private static Task
        CompletePreflightAsync(
            FunctionContext functionContext)
    {
        functionContext
            .GetInvocationResult()
            .Value =
                new StatusCodeResult(
                    StatusCodes
                        .Status204NoContent);

        return Task.CompletedTask;
    }

    private async Task<bool>
        ApplyRateLimitAsync(
            FunctionContext functionContext,
            HttpContext httpContext,
            string functionName,
            ApiRequestIdentity? identity)
    {
        // Local development-only rate-limit bypass.
        // This remains off unless local.settings.json
        // explicitly sets it to true.
        if (string.Equals(
                Environment.GetEnvironmentVariable(
                    "DISABLE_API_RATE_LIMITING"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var result =
            await _rateLimiter.CheckAsync(
                functionName,
                httpContext.Request,
                identity,
                httpContext.RequestAborted);

        if (result.IsAllowed)
        {
            return true;
        }

        var retryAfterSeconds =
            Math.Max(
                1,
                (int)Math.Ceiling(
                    result
                        .RetryAfter
                        .TotalSeconds));

        if (!httpContext.Response.HasStarted)
        {
            httpContext.Response.Headers[
                HeaderNames.RetryAfter] =
                retryAfterSeconds.ToString();
        }

        await RejectAsync(
            functionContext,
            httpContext,
            StatusCodes
                .Status429TooManyRequests,
            "Too many requests. Please try again later.");

        return false;
    }
}