using EMA.Api.Services.Navigation;
using System.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Navigation;

public sealed class GetPublicRouteAccess
{
    private const int BrochureCategoryID = 10;

    private readonly ILogger<GetPublicRouteAccess> _logger;
    private readonly string _connectionString;

    public GetPublicRouteAccess(
        ILogger<GetPublicRouteAccess> logger)
    {
        _logger = logger;

        _connectionString =
            Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString.");
    }

    [Function("GetPublicRouteAccess")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,             "get",
            Route = "navigation/public-route-access")]
        HttpRequest request)
    {
        var requestedPath =
            NormalizePath(
                request.Query["path"].FirstOrDefault());

        _logger.LogInformation(
            "GetPublicRouteAccess triggered for path {Path}.",
            requestedPath);

        try
        {
            await using var connection =
                new SqlConnection(
                    _connectionString);

            await connection.OpenAsync();

            var matchedRoute =
                await GetBestMatchingPublicMenuItemAsync(
                    connection,
                    requestedPath);

            return new OkObjectResult(
                matchedRoute is null
                    ? RouteAccessResult.Denied(
                        "The route is not configured as a public brochure route.")
                    : RouteAccessResult.Allowed(
                        "Allowed public brochure route.",
                        matchedRoute));
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error checking public route access for path {Path}.",
                requestedPath);

            return new ObjectResult(new
            {
                message =
                    "Unable to check public route access."
            })
            {
                StatusCode =
                    StatusCodes.Status500InternalServerError
            };
        }
    }

    private static async Task<MenuRouteMatch?>
        GetBestMatchingPublicMenuItemAsync(
            SqlConnection connection,
            string requestedPath)
    {
        var resourceTablesExist =
            await MenuItemResourceQuery.TablesExistAsync(
                connection);

        const string baseSql = """
            WITH PublicMenuRoutes AS
            (
                SELECT
                    mi.MenuItemID,
                    mi.MenuItemCategoryID,
                    mi.URL,
                    LOWER(
                        CASE
                            WHEN LEFT(LTRIM(RTRIM(mi.URL)), 1) = '/'
                                THEN LTRIM(RTRIM(mi.URL))
                            ELSE '/' + LTRIM(RTRIM(mi.URL))
                        END
                    ) AS NormalizedUrl
                FROM dbo.MenuItem mi
                WHERE mi.MenuItemCategoryID = @BrochureCategoryID
                  AND mi.IsActive = 'Y'
                  AND NULLIF(LTRIM(RTRIM(mi.URL)), '') IS NOT NULL
            )
            SELECT TOP 1
                MenuItemID,
                MenuItemCategoryID,
                URL,
                NormalizedUrl
            FROM PublicMenuRoutes
            WHERE @RequestedPath = NormalizedUrl
               OR @RequestedPath LIKE NormalizedUrl + '/%'
            ORDER BY
                CASE WHEN @RequestedPath = NormalizedUrl THEN 0 ELSE 1 END,
                LEN(NormalizedUrl) DESC,
                MenuItemID;
            """;

        const string resourceSql = """
            WITH PublicMenuRoutes AS
            (
                SELECT
                    mi.MenuItemID,
                    mi.MenuItemCategoryID,
                    mi.URL,
                    LOWER(
                        CASE
                            WHEN LEFT(LTRIM(RTRIM(mi.URL)), 1) = '/'
                                THEN LTRIM(RTRIM(mi.URL))
                            ELSE '/' + LTRIM(RTRIM(mi.URL))
                        END
                    ) AS NormalizedUrl
                FROM dbo.MenuItem mi
                WHERE mi.MenuItemCategoryID = @BrochureCategoryID
                  AND mi.IsActive = 'Y'
                  AND NULLIF(LTRIM(RTRIM(mi.URL)), '') IS NOT NULL

                UNION ALL

                SELECT
                    mi.MenuItemID,
                    mi.MenuItemCategoryID,
                    mir.ScreenURL AS URL,
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
                WHERE mi.MenuItemCategoryID = @BrochureCategoryID
                  AND mi.IsActive = 'Y'
                  AND UPPER(mir.MenuItemResourceTypeID) = 'W'
                  AND NULLIF(LTRIM(RTRIM(mir.ScreenURL)), '') IS NOT NULL
            )
            SELECT TOP 1
                MenuItemID,
                MenuItemCategoryID,
                URL,
                NormalizedUrl
            FROM PublicMenuRoutes
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
            "@BrochureCategoryID",
            SqlDbType.Int)
            .Value = BrochureCategoryID;

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
                ?? string.Empty
        };
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
            return new RouteAccessResult
            {
                IsAllowed = true,
                Reason = reason,
                MenuItemID = match.MenuItemID,
                CategoryID = match.CategoryID,
                Url = match.Url,
                IsConfiguredLandingPage = false
            };
        }

        public static RouteAccessResult Denied(
            string reason)
        {
            return new RouteAccessResult
            {
                IsAllowed = false,
                Reason = reason
            };
        }
    }
}
