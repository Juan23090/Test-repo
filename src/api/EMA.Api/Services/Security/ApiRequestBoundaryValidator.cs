using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;

namespace EMA.Api.Services.Security;

public sealed class ApiRequestBoundaryValidator
{
    private static readonly HashSet<string>
        OrganizationIDNames =
        new(
            new[]
            {
                "EMAOrganizationID",
                "OrganizationID",
                "OrgID",
                "SandboxOrganizationID"
            },
            StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string>
        DonorIDNames =
        new(
            new[]
            {
                "EMADonorID",
                "DonorID"
            },
            StringComparer.OrdinalIgnoreCase);

    private readonly int
        _platformOrganizationID;

    private readonly int
        _platformCategoryID;

    private readonly HashSet<int>
        _organizationAdministratorRoleIDs;

    private readonly
        ITenantResourceAuthorizationService
        _tenantResourceAuthorizationService;

    public ApiRequestBoundaryValidator(
        IConfiguration configuration,
        ITenantResourceAuthorizationService
            tenantResourceAuthorizationService)
    {
        _tenantResourceAuthorizationService =
            tenantResourceAuthorizationService;

        var isProduction =
            IsProduction();

        _platformOrganizationID =
            ReadPositiveInteger(
                configuration,
                "PLATFORM_ORGANIZATION_ID",
                defaultValue:
                    isProduction
                        ? 0
                        : 1);

        _platformCategoryID =
            ReadPositiveInteger(
                configuration,
                "PLATFORM_ADMIN_CATEGORY_ID",
                defaultValue:
                    isProduction
                        ? 0
                        : 90);

        if (_platformOrganizationID <= 0 ||
            _platformCategoryID <= 0)
        {
            throw new InvalidOperationException(
                "PLATFORM_ORGANIZATION_ID and PLATFORM_ADMIN_CATEGORY_ID must be configured as positive integers in production.");
        }

        _organizationAdministratorRoleIDs =
            ReadPositiveIntegerSet(
                configuration[
                    "ORGANIZATION_ADMIN_ROLE_IDS"]);

        if (isProduction &&
            _organizationAdministratorRoleIDs
                .Count == 0)
        {
            throw new InvalidOperationException(
                "ORGANIZATION_ADMIN_ROLE_IDS must list at least one role in production.");
        }

        if (!isProduction &&
            _organizationAdministratorRoleIDs
                .Count == 0)
        {
            _organizationAdministratorRoleIDs
                .Add(
                    10);
        }
    }

    public bool IsPlatformAdministrator(
        ApiRequestIdentity identity)
    {
        return identity.ActorType ==
                   ApiActorType.OrganizationUser &&
               identity.EMAOrganizationID ==
                   _platformOrganizationID &&
               identity.AllowedCategoryIDs.Contains(
                   _platformCategoryID);
    }

    public int PlatformAdministratorCategoryID =>
        _platformCategoryID;

    public int PlatformOrganizationID =>
        _platformOrganizationID;

    public bool
        IsOrganizationAdministrator(
            ApiRequestIdentity identity)
    {
        return identity.ActorType ==
                   ApiActorType
                       .OrganizationUser &&
               (
                   IsPlatformAdministrator(
                       identity) ||
                   identity.RoleIDs.Any(
                       _organizationAdministratorRoleIDs
                           .Contains)
               );
    }

    public async Task<ApiBoundaryValidationResult>
        ValidateAsync(
            FunctionContext functionContext,
            HttpContext httpContext,
            ApiRequestIdentity identity,
            ApiEndpointPolicy policy)
    {
        if (httpContext.Request.ContentLength >
            policy.MaximumRequestBodyBytes)
        {
            return ApiBoundaryValidationResult
                .Reject(
                    StatusCodes
                        .Status413PayloadTooLarge,
                    "The request body is too large.");
        }

        var isPlatformAdministrator =
            IsPlatformAdministrator(
                identity);

        var mayCrossTenant =
            isPlatformAdministrator &&
            (
                policy.Access ==
                    ApiEndpointAccess
                        .PlatformAdministrator ||
                policy.AllowPlatformCrossTenant
            );

        if (!TryBuildRouteValues(
                functionContext,
                httpContext.Request,
                out var routeValues))
        {
            return ApiBoundaryValidationResult
                .Reject(
                    StatusCodes.Status400BadRequest,
                    "The request route is invalid.");
        }

        var routeResult =
            ValidateRouteValues(
                routeValues,
                identity,
                mayCrossTenant);

        if (!routeResult.IsAllowed)
        {
            return routeResult;
        }

        var queryResult =
            ValidateNameValuePairs(
                httpContext.Request.Query,
                identity,
                mayCrossTenant);

        if (!queryResult.IsAllowed)
        {
            return queryResult;
        }

        if (httpContext.Request.ContentLength == 0 ||
            !IsJsonContentType(
                httpContext.Request.ContentType))
        {
            if (!mayCrossTenant &&
                !await _tenantResourceAuthorizationService
                    .AreResourcesOwnedByTenantAsync(
                        functionContext
                            .FunctionDefinition
                            .Name,
                        routeValues,
                        httpContext
                            .Request
                            .Query,
                        null,
                        identity,
                        httpContext
                            .RequestAborted))
            {
                return ApiBoundaryValidationResult
                    .Reject(
                        StatusCodes
                            .Status403Forbidden,
                        "A referenced resource is not available to this organization.");
            }

            return ApiBoundaryValidationResult
                .Allow();
        }

        try
        {
            httpContext.Request.EnableBuffering(
                bufferThreshold: 64 * 1024,
                bufferLimit:
                    policy.MaximumRequestBodyBytes);

            using var document =
                await JsonDocument.ParseAsync(
                    httpContext.Request.Body,
                    new JsonDocumentOptions
                    {
                        AllowTrailingCommas =
                            false,

                        CommentHandling =
                            JsonCommentHandling.Disallow,

                        MaxDepth =
                            32
                    },
                    httpContext.RequestAborted);

            httpContext.Request.Body.Position =
                0;

            var jsonResult =
                ValidateJsonElement(
                document.RootElement,
                identity,
                mayCrossTenant);

            if (!jsonResult.IsAllowed)
            {
                return jsonResult;
            }

            if (!mayCrossTenant &&
                !await _tenantResourceAuthorizationService
                    .AreResourcesOwnedByTenantAsync(
                        functionContext
                            .FunctionDefinition
                            .Name,
                        routeValues,
                        httpContext
                            .Request
                            .Query,
                        document.RootElement,
                        identity,
                        httpContext
                            .RequestAborted))
            {
                return ApiBoundaryValidationResult
                    .Reject(
                        StatusCodes
                            .Status403Forbidden,
                        "A referenced resource is not available to this organization.");
            }

            return ApiBoundaryValidationResult
                .Allow();
        }
        catch (IOException)
        {
            return ApiBoundaryValidationResult
                .Reject(
                    StatusCodes
                        .Status413PayloadTooLarge,
                    "The request body is too large.");
        }
        catch (JsonException)
        {
            if (httpContext.Request.Body.CanSeek)
            {
                httpContext.Request.Body.Position =
                    0;
            }

            return ApiBoundaryValidationResult
                .Reject(
                    StatusCodes.Status400BadRequest,
                    "The JSON request body is invalid.");
        }
    }

    private static ApiBoundaryValidationResult
        ValidateRouteValues(
            IReadOnlyDictionary<
                string,
                object?> routeValues,
            ApiRequestIdentity identity,
            bool isPlatformAdministrator)
    {
        foreach (var pair in routeValues)
        {
            var validationResult =
                ValidateNamedValue(
                    pair.Key,
                    pair.Value?.ToString(),
                    identity,
                    isPlatformAdministrator,
                    enforceUserID: true);

            if (!validationResult.IsAllowed)
            {
                return validationResult;
            }
        }

        return ApiBoundaryValidationResult
            .Allow();
    }

    private static bool TryBuildRouteValues(
        FunctionContext functionContext,
        HttpRequest request,
        out IReadOnlyDictionary<
            string,
            object?> routeValues)
    {
        var combined =
            new Dictionary<
                string,
                object?>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var pair in functionContext
                     .BindingContext
                     .BindingData)
        {
            combined[pair.Key] =
                pair.Value;
        }

        foreach (var pair in request.RouteValues)
        {
            if (combined.TryGetValue(
                    pair.Key,
                    out var existingValue) &&
                existingValue is not null &&
                pair.Value is not null &&
                !string.Equals(
                    existingValue.ToString(),
                    pair.Value.ToString(),
                    StringComparison.OrdinalIgnoreCase))
            {
                routeValues =
                    combined;

                return false;
            }

            combined[pair.Key] =
                pair.Value;
        }

        routeValues =
            combined;

        return true;
    }

    private static ApiBoundaryValidationResult
        ValidateNameValuePairs(
            IEnumerable<
                KeyValuePair<
                    string,
                    Microsoft.Extensions.Primitives.StringValues>>
                values,
            ApiRequestIdentity identity,
            bool isPlatformAdministrator)
    {
        foreach (var pair in values)
        {
            foreach (var value in pair.Value)
            {
                var validationResult =
                    ValidateNamedValue(
                        pair.Key,
                        value,
                        identity,
                        isPlatformAdministrator,
                        enforceUserID: true);

                if (!validationResult.IsAllowed)
                {
                    return validationResult;
                }
            }
        }

        return ApiBoundaryValidationResult
            .Allow();
    }

    private static ApiBoundaryValidationResult
        ValidateJsonElement(
            JsonElement element,
            ApiRequestIdentity identity,
            bool isPlatformAdministrator)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in
                         element.EnumerateObject())
                {
                    if (property.Value.ValueKind is
                            JsonValueKind.Number or
                            JsonValueKind.String)
                    {
                        var value =
                            property.Value.ValueKind ==
                            JsonValueKind.Number
                                ? property.Value
                                    .GetRawText()
                                : property.Value
                                    .GetString();

                        var validationResult =
                            ValidateNamedValue(
                                property.Name,
                                value,
                                identity,
                                isPlatformAdministrator,
                                enforceUserID: false);

                        if (!validationResult
                                .IsAllowed)
                        {
                            return validationResult;
                        }
                    }

                    var childResult =
                        ValidateJsonElement(
                            property.Value,
                            identity,
                            isPlatformAdministrator);

                    if (!childResult.IsAllowed)
                    {
                        return childResult;
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in
                         element.EnumerateArray())
                {
                    var childResult =
                        ValidateJsonElement(
                            item,
                            identity,
                            isPlatformAdministrator);

                    if (!childResult.IsAllowed)
                    {
                        return childResult;
                    }
                }

                break;
        }

        return ApiBoundaryValidationResult
            .Allow();
    }

    private static ApiBoundaryValidationResult
        ValidateNamedValue(
            string name,
            string? rawValue,
            ApiRequestIdentity identity,
            bool isPlatformAdministrator,
            bool enforceUserID)
    {
        if (string.IsNullOrWhiteSpace(
                rawValue))
        {
            return ApiBoundaryValidationResult
                .Allow();
        }

        var isOrganizationID =
            OrganizationIDNames.Contains(
                name);

        var isDonorSessionID =
            identity.ActorType ==
                ApiActorType.Donor &&
            DonorIDNames.Contains(
                name);

        var isOrganizationUserID =
            identity.ActorType ==
                ApiActorType.OrganizationUser &&
            enforceUserID &&
            name.Equals(
                "UserID",
                StringComparison.OrdinalIgnoreCase);

        if (!long.TryParse(
                rawValue,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var value) ||
            value <= 0)
        {
            return isOrganizationID ||
                   isDonorSessionID ||
                   isOrganizationUserID
                ? ApiBoundaryValidationResult
                    .Reject(
                        StatusCodes.Status400BadRequest,
                        "A referenced identity value is invalid.")
                : ApiBoundaryValidationResult
                    .Allow();
        }

        if (isOrganizationID &&
            !isPlatformAdministrator &&
            value != identity.EMAOrganizationID)
        {
            return ApiBoundaryValidationResult
                .Reject(
                    StatusCodes.Status403Forbidden,
                    "The requested organization is not available to this session.");
        }

        if (isDonorSessionID &&
            value !=
                identity.DonorID)
        {
            return ApiBoundaryValidationResult
                .Reject(
                    StatusCodes.Status403Forbidden,
                    "The requested donor is not available to this session.");
        }

        if (isOrganizationUserID &&
            !isPlatformAdministrator &&
            value != identity.UserID)
        {
            return ApiBoundaryValidationResult
                .Reject(
                    StatusCodes.Status403Forbidden,
                    "The requested user is not available to this session.");
        }

        return ApiBoundaryValidationResult
            .Allow();
    }

    private static bool IsJsonContentType(
        string? contentType)
    {
        if (string.IsNullOrWhiteSpace(
                contentType))
        {
            return false;
        }

        var mediaType =
            contentType
                .Split(
                    ';',
                    2,
                    StringSplitOptions.TrimEntries)[0];

        return mediaType.Equals(
                   "application/json",
                   StringComparison.OrdinalIgnoreCase) ||
               mediaType.EndsWith(
                   "+json",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static int ReadPositiveInteger(
        IConfiguration configuration,
        string settingName,
        int defaultValue)
    {
        return int.TryParse(
                   configuration[settingName],
                   out var value) &&
               value > 0
            ? value
            : defaultValue;
    }

    private static HashSet<int>
        ReadPositiveIntegerSet(
            string? rawValues)
    {
        var values =
            new HashSet<int>();

        if (string.IsNullOrWhiteSpace(
                rawValues))
        {
            return values;
        }

        foreach (var rawValue in rawValues
                     .Split(
                         ',',
                         StringSplitOptions
                             .RemoveEmptyEntries |
                         StringSplitOptions
                             .TrimEntries))
        {
            if (!int.TryParse(
                    rawValue,
                    out var value) ||
                value <= 0)
            {
                throw new InvalidOperationException(
                    "ORGANIZATION_ADMIN_ROLE_IDS contains an invalid role ID.");
            }

            values.Add(
                value);
        }

        return values;
    }

    private static bool IsProduction()
    {
        return ApiRuntimeEnvironment
            .IsProductionLike;
    }
}

public sealed record ApiBoundaryValidationResult(
    bool IsAllowed,
    int StatusCode,
    string Message)
{
    public static ApiBoundaryValidationResult
        Allow()
    {
        return new ApiBoundaryValidationResult(
            true,
            StatusCodes.Status200OK,
            string.Empty);
    }

    public static ApiBoundaryValidationResult
        Reject(
            int statusCode,
            string message)
    {
        return new ApiBoundaryValidationResult(
            false,
            statusCode,
            message);
    }
}
