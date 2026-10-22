using EMA.Api.Services.Navigation;
using EMA.Api.Services.Security;
using System.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Navigation;

public sealed class GetRouteAccess
{
    private const int BrochureCategoryID = 10;

    private readonly ILogger<GetRouteAccess> _logger;
    private readonly ApiRequestBoundaryValidator _authorization;
    private readonly string _connectionString;

    public GetRouteAccess(
        ILogger<GetRouteAccess> logger,
        ApiRequestBoundaryValidator authorization)
    {
        _logger = logger;
        _authorization = authorization;

        _connectionString =
            Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString.");
    }

    [Function("GetRouteAccess")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,             "get",
            Route = "organization/{orgID:int}/users/{userID:int}/route-access")]
        HttpRequest request,
        int orgID,
        int userID)
    {
        var requestedPath =
            NormalizePath(
                request.Query["path"].FirstOrDefault());

        _logger.LogInformation(
            "GetRouteAccess triggered for org {OrgID}, user {UserID}, path {Path}.",
            orgID,
            userID,
            requestedPath);

        var identity =
            request.GetRequiredApiIdentity();

        var isAdministrator =
            _authorization.IsOrganizationAdministrator(
                identity);

        try
        {
            await using var connection =
                new SqlConnection(
                    _connectionString);

            await connection.OpenAsync();

            if (!await UserBelongsToOrganizationAsync(
                    connection,
                    orgID,
                    userID))
            {
                return new OkObjectResult(
                    RouteAccessResult.Denied(
                        "The user is not active in the selected organization."));
            }

            var matchedRoute =
                await GetBestMatchingMenuItemAsync(
                    connection,
                    requestedPath);

            if (matchedRoute is null)
            {
                return new OkObjectResult(
                    RouteAccessResult.Denied(
                        "The route is not configured as an active menu item."));
            }

            var categoryAllowed =
                await UserCanAccessCategoryAsync(
                    connection,
                    orgID,
                    userID,
                    matchedRoute.CategoryID);

            if (!categoryAllowed)
            {
                return new OkObjectResult(
                    RouteAccessResult.Denied(
                        "The user cannot access this menu category.",
                        matchedRoute));
            }

            if (matchedRoute.IsConfiguredLandingPage)
            {
                return new OkObjectResult(
                    RouteAccessResult.Allowed(
                        "Allowed category landing page.",
                        matchedRoute));
            }

            var menuItemAllowed =
                await UserCanAccessMenuItemAsync(
                    connection,
                    orgID,
                    userID,
                    matchedRoute.MenuItemID,
                    matchedRoute.CategoryID,
                    isAdministrator);

            return new OkObjectResult(
                menuItemAllowed
                    ? RouteAccessResult.Allowed(
                        "Allowed role menu item.",
                        matchedRoute)
                    : RouteAccessResult.Denied(
                        "The user does not have access to this menu item.",
                        matchedRoute));
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error checking route access for org {OrgID}, user {UserID}, path {Path}.",
                orgID,
                userID,
                requestedPath);

            return new ObjectResult(new
            {
                message =
                    "Unable to check route access."
            })
            {
                StatusCode =
                    StatusCodes.Status500InternalServerError
            };
        }
    }

    private static async Task<bool> UserBelongsToOrganizationAsync(
        SqlConnection connection,
        int orgID,
        int userID)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM dbo.OrganizationUser ou
            INNER JOIN dbo.[User] u
                ON ou.UserID = u.UserID
            WHERE ou.EMAOrganizationID = @OrgID
              AND ou.UserID = @UserID
              AND u.IsActive = 1;
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters.Add(
            "@OrgID",
            SqlDbType.Int)
            .Value = orgID;

        command.Parameters.Add(
            "@UserID",
            SqlDbType.Int)
            .Value = userID;

        var count =
            Convert.ToInt32(
                await command.ExecuteScalarAsync());

        return count > 0;
    }

    private static async Task<MenuRouteMatch?>
        GetBestMatchingMenuItemAsync(
            SqlConnection connection,
            string requestedPath)
    {
        var resourceTablesExist =
            await MenuItemResourceQuery.TablesExistAsync(
                connection);

        const string baseSql = """
            WITH ActiveMenuRoutes AS
            (
                SELECT
                    mi.MenuItemID,
                    mi.MenuItemCategoryID,
                    mi.URL,
                    CASE
                        WHEN UPPER(COALESCE(NULLIF(LTRIM(RTRIM(mi.DisplayControlID)), ''), 'M')) = 'L'
                            THEN CAST(1 AS bit)
                        ELSE CAST(0 AS bit)
                    END AS IsConfiguredLandingPage,
                    LOWER(
                        CASE
                            WHEN LEFT(LTRIM(RTRIM(mi.URL)), 1) = '/'
                                THEN LTRIM(RTRIM(mi.URL))
                            ELSE '/' + LTRIM(RTRIM(mi.URL))
                        END
                    ) AS NormalizedUrl
                FROM dbo.MenuItem mi
                WHERE mi.IsActive = 'Y'
                  AND NULLIF(LTRIM(RTRIM(mi.URL)), '') IS NOT NULL
            )
            SELECT TOP 1
                MenuItemID,
                MenuItemCategoryID,
                URL,
                IsConfiguredLandingPage,
                NormalizedUrl
            FROM ActiveMenuRoutes
            WHERE @RequestedPath = NormalizedUrl
               OR @RequestedPath LIKE NormalizedUrl + '/%'
            ORDER BY
                CASE WHEN @RequestedPath = NormalizedUrl THEN 0 ELSE 1 END,
                LEN(NormalizedUrl) DESC,
                MenuItemID;
            """;

        const string resourceSql = """
            WITH ActiveMenuRoutes AS
            (
                SELECT
                    mi.MenuItemID,
                    mi.MenuItemCategoryID,
                    mi.URL,
                    CASE
                        WHEN UPPER(COALESCE(NULLIF(LTRIM(RTRIM(mi.DisplayControlID)), ''), 'M')) = 'L'
                            THEN CAST(1 AS bit)
                        ELSE CAST(0 AS bit)
                    END AS IsConfiguredLandingPage,
                    LOWER(
                        CASE
                            WHEN LEFT(LTRIM(RTRIM(mi.URL)), 1) = '/'
                                THEN LTRIM(RTRIM(mi.URL))
                            ELSE '/' + LTRIM(RTRIM(mi.URL))
                        END
                    ) AS NormalizedUrl
                FROM dbo.MenuItem mi
                WHERE mi.IsActive = 'Y'
                  AND NULLIF(LTRIM(RTRIM(mi.URL)), '') IS NOT NULL

                UNION ALL

                SELECT
                    mi.MenuItemID,
                    mi.MenuItemCategoryID,
                    mir.ScreenURL AS URL,
                    CAST(0 AS bit) AS IsConfiguredLandingPage,
                    LOWER(
                        CASE
                            WHEN LEFT(LTRIM(RTRIM(mir.ScreenURL)), 1) = '/'
                                THEN LTRIM(RTRIM(mir.ScreenURL))
                            ELSE '/' + LTRIM(RTRIM(mir.ScreenURL))
                        END
                    ) AS NormalizedUrl
                FROM dbo.MenuItemResource mir
                INNER JOIN dbo.MenuItem mi
                    ON mir.MenuItemID = mi.MenuItemID
                WHERE mi.IsActive = 'Y'
                  AND UPPER(mir.MenuItemResourceTypeID) = 'W'
                  AND NULLIF(LTRIM(RTRIM(mir.ScreenURL)), '') IS NOT NULL
            )
            SELECT TOP 1
                MenuItemID,
                MenuItemCategoryID,
                URL,
                IsConfiguredLandingPage,
                NormalizedUrl
            FROM ActiveMenuRoutes
            WHERE @RequestedPath = NormalizedUrl
               OR @RequestedPath LIKE NormalizedUrl + '/%'
            ORDER BY
                CASE WHEN @RequestedPath = NormalizedUrl THEN 0 ELSE 1 END,
                LEN(NormalizedUrl) DESC,
                MenuItemID;
            """;

        await using var command =
            new SqlCommand(
                resourceTablesExist ? resourceSql : baseSql,
                connection);

        command.Parameters.Add(
            "@RequestedPath",
            SqlDbType.NVarChar,
            500)
            .Value = requestedPath;

        await using var reader =
            await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new MenuRouteMatch
        {
            MenuItemID =
                Convert.ToInt32(reader["MenuItemID"]),
            CategoryID =
                Convert.ToInt32(reader["MenuItemCategoryID"]),
            Url =
                reader["NormalizedUrl"]?.ToString()
                ?? string.Empty,
            IsConfiguredLandingPage =
                reader["IsConfiguredLandingPage"] is bool boolValue
                    ? boolValue
                    : Convert.ToInt32(
                        reader["IsConfiguredLandingPage"]) == 1
        };
    }

    private static async Task<bool> UserCanAccessCategoryAsync(
        SqlConnection connection,
        int orgID,
        int userID,
        int categoryID)
    {
        if (categoryID == BrochureCategoryID)
        {
            return true;
        }

        const string sql = """
            SELECT COUNT(1)
            FROM dbo.MenuItem mi
            INNER JOIN dbo.RoleMenuItem rmi
                ON mi.MenuItemID = rmi.MenuItemID
            INNER JOIN dbo.OrganizationUserRole ur
                ON rmi.RoleID = ur.RoleID
               AND ur.EMAOrganizationID =
                   @OrgID
            INNER JOIN dbo.OrganizationUser ou
                ON ur.UserID = ou.UserID
            INNER JOIN dbo.[User] u
                ON ur.UserID = u.UserID
            INNER JOIN dbo.[Role] r
                ON ur.RoleID = r.RoleID
            WHERE ou.EMAOrganizationID = @OrgID
              AND ur.UserID = @UserID
              AND mi.MenuItemCategoryID = @CategoryID
              AND mi.IsActive = 'Y'
              AND u.IsActive = 1
              AND r.IsActive = 'Y';
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters.Add(
            "@OrgID",
            SqlDbType.Int)
            .Value = orgID;

        command.Parameters.Add(
            "@UserID",
            SqlDbType.Int)
            .Value = userID;

        command.Parameters.Add(
            "@CategoryID",
            SqlDbType.Int)
            .Value = categoryID;

        var count =
            Convert.ToInt32(
                await command.ExecuteScalarAsync());

        return count > 0;
    }

    private static async Task<bool> UserCanAccessMenuItemAsync(
        SqlConnection connection,
        int orgID,
        int userID,
        int menuItemID,
        int categoryID,
        bool isAdministrator)
    {
        if (categoryID == BrochureCategoryID ||
            isAdministrator)
        {
            return true;
        }

        const string sql = """
            SELECT COUNT(1)
            FROM dbo.MenuItem mi
            INNER JOIN dbo.RoleMenuItem rmi
                ON mi.MenuItemID = rmi.MenuItemID
            INNER JOIN dbo.OrganizationUserRole ur
                ON rmi.RoleID = ur.RoleID
               AND ur.EMAOrganizationID =
                   @OrgID
            INNER JOIN dbo.OrganizationUser ou
                ON ur.UserID = ou.UserID
            INNER JOIN dbo.[User] u
                ON ur.UserID = u.UserID
            INNER JOIN dbo.[Role] r
                ON ur.RoleID = r.RoleID
            WHERE ou.EMAOrganizationID = @OrgID
              AND ur.UserID = @UserID
              AND mi.MenuItemID = @MenuItemID
              AND mi.IsActive = 'Y'
              AND u.IsActive = 1
              AND r.IsActive = 'Y';
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters.Add(
            "@OrgID",
            SqlDbType.Int)
            .Value = orgID;

        command.Parameters.Add(
            "@UserID",
            SqlDbType.Int)
            .Value = userID;

        command.Parameters.Add(
            "@MenuItemID",
            SqlDbType.Int)
            .Value = menuItemID;

        var count =
            Convert.ToInt32(
                await command.ExecuteScalarAsync());

        return count > 0;
    }

    private static string NormalizePath(
        string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/";
        }

        var trimmed =
            path.Trim();

        var queryIndex =
            trimmed.IndexOf(
                '?',
                StringComparison.Ordinal);

        if (queryIndex >= 0)
        {
            trimmed =
                trimmed[..queryIndex];
        }

        if (!trimmed.StartsWith(
                "/",
                StringComparison.Ordinal))
        {
            trimmed =
                $"/{trimmed}";
        }

        return trimmed.Length > 1
            ? trimmed.TrimEnd('/').ToLowerInvariant()
            : trimmed.ToLowerInvariant();
    }

    private sealed class MenuRouteMatch
    {
        public int MenuItemID { get; set; }

        public int CategoryID { get; set; }

        public string Url { get; set; } =
            string.Empty;

        public bool IsConfiguredLandingPage { get; set; }
    }

    private sealed class RouteAccessResult
    {
        public bool IsAllowed { get; set; }

        public string Reason { get; set; } =
            string.Empty;

        public int? MenuItemID { get; set; }

        public int? CategoryID { get; set; }

        public string Url { get; set; } =
            string.Empty;

        public bool IsConfiguredLandingPage { get; set; }

        public static RouteAccessResult Allowed(
            string reason,
            MenuRouteMatch match)
        {
            return FromMatch(
                true,
                reason,
                match);
        }

        public static RouteAccessResult Denied(
            string reason,
            MenuRouteMatch? match = null)
        {
            return FromMatch(
                false,
                reason,
                match);
        }

        private static RouteAccessResult FromMatch(
            bool isAllowed,
            string reason,
            MenuRouteMatch? match)
        {
            return new RouteAccessResult
            {
                IsAllowed = isAllowed,
                Reason = reason,
                MenuItemID = match?.MenuItemID,
                CategoryID = match?.CategoryID,
                Url = match?.Url ?? string.Empty,
                IsConfiguredLandingPage =
                    match?.IsConfiguredLandingPage ?? false
            };
        }
    }
}
