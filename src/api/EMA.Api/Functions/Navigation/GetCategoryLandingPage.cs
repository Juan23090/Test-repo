using EMA.Api.Services.Security;
using System.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Navigation;

public sealed class GetCategoryLandingPage
{
    private const int BrochureCategoryID = 10;

    private readonly ILogger<GetCategoryLandingPage> _logger;
    private readonly ApiRequestBoundaryValidator _authorization;
    private readonly string _connectionString;

    public GetCategoryLandingPage(
        ILogger<GetCategoryLandingPage> logger,
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

    [Function("GetCategoryLandingPage")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "organization/{orgID:int}/users/{userID:int}/categories/{categoryID:int}/landing-page")]
        HttpRequest request,
        int orgID,
        int userID,
        int categoryID)
    {
        _logger.LogInformation(
            "GetCategoryLandingPage triggered for org {OrgID}, user {UserID}, category {CategoryID}.",
            orgID,
            userID,
            categoryID);

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
                return new ObjectResult(new
                {
                    message =
                        "The user is not active in the selected organization."
                })
                {
                    StatusCode =
                        StatusCodes.Status403Forbidden
                };
            }

            if (!await UserCanAccessCategoryAsync(
                    connection,
                    orgID,
                    userID,
                    categoryID))
            {
                return new ObjectResult(new
                {
                    message =
                        "The user cannot access this menu category."
                })
                {
                    StatusCode =
                        StatusCodes.Status403Forbidden
                };
            }

            var categoryName =
                await GetCategoryNameAsync(
                    connection,
                    categoryID);

            if (categoryName is null)
            {
                return new NotFoundObjectResult(
                    new
                    {
                        message =
                            "The menu category was not found."
                    });
            }

            var firstLandingPage =
                await GetFirstLandingPageAsync(
                    connection,
                    orgID,
                    userID,
                    categoryID,
                    isAdministrator);

            return new OkObjectResult(
                firstLandingPage ??
                CategoryLandingPageDto.Fallback(
                    categoryID,
                    categoryName));
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error loading category landing page for org {OrgID}, user {UserID}, category {CategoryID}.",
                orgID,
                userID,
                categoryID);

            return new ObjectResult(new
            {
                message =
                    "Unable to load category landing page."
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

    private static async Task<string?> GetCategoryNameAsync(
        SqlConnection connection,
        int categoryID)
    {
        const string sql = """
            SELECT TOP (1)
                Description
            FROM dbo.MenuItemCategory
            WHERE MenuItemCategoryID = @CategoryID;
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters.Add(
            "@CategoryID",
            SqlDbType.Int)
            .Value = categoryID;

        var result =
            await command.ExecuteScalarAsync();

        return result is null ||
               result == DBNull.Value
            ? null
            : result.ToString()?.Trim();
    }

    private static async Task<CategoryLandingPageDto?>
        GetFirstLandingPageAsync(
            SqlConnection connection,
            int orgID,
            int userID,
            int categoryID,
            bool isAdministrator)
    {
        var isBrochureRequest =
            categoryID == BrochureCategoryID;

        var sql =
            isBrochureRequest ||
            isAdministrator
                ? """
                  SELECT TOP 1
                      mic.MenuItemCategoryID,
                      mic.Description AS CategoryName,
                      mi.MenuItemID,
                      mi.MenuName,
                      mi.ScreenTitle,
                      mi.URL
                  FROM dbo.MenuItemCategory mic
                  INNER JOIN dbo.MenuItem mi
                      ON mic.MenuItemCategoryID = mi.MenuItemCategoryID
                  WHERE mic.MenuItemCategoryID = @CategoryID
                    AND mi.IsActive = 'Y'
                    AND UPPER(COALESCE(NULLIF(LTRIM(RTRIM(mi.DisplayControlID)), ''), 'M')) = 'L'
                    AND NULLIF(LTRIM(RTRIM(mi.URL)), '') IS NOT NULL
                  ORDER BY
                      mi.MenuItemRowID,
                      mi.MenuItemID;
                  """
                : """
                  SELECT TOP 1
                      mic.MenuItemCategoryID,
                      mic.Description AS CategoryName,
                      mi.MenuItemID,
                      mi.MenuName,
                      mi.ScreenTitle,
                      mi.URL
                  FROM dbo.MenuItemCategory mic
                  INNER JOIN dbo.MenuItem mi
                      ON mic.MenuItemCategoryID = mi.MenuItemCategoryID
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
                    AND mic.MenuItemCategoryID = @CategoryID
                    AND mi.IsActive = 'Y'
                    AND UPPER(COALESCE(NULLIF(LTRIM(RTRIM(mi.DisplayControlID)), ''), 'M')) = 'L'
                    AND u.IsActive = 1
                    AND r.IsActive = 'Y'
                    AND NULLIF(LTRIM(RTRIM(mi.URL)), '') IS NOT NULL
                  ORDER BY
                      mi.MenuItemRowID,
                      mi.MenuItemID;
                  """;

        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters.Add(
            "@CategoryID",
            SqlDbType.Int)
            .Value = categoryID;

        if (!isBrochureRequest &&
            !isAdministrator)
        {
            command.Parameters.Add(
                "@OrgID",
                SqlDbType.Int)
                .Value = orgID;

            command.Parameters.Add(
                "@UserID",
                SqlDbType.Int)
                .Value = userID;
        }

        await using var reader =
            await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        return ReadLandingPage(
            reader,
            isConfiguredLandingPage: true,
            isFallback: false);
    }

    private static CategoryLandingPageDto ReadLandingPage(
        SqlDataReader reader,
        bool isConfiguredLandingPage,
        bool isFallback)
    {
        return new CategoryLandingPageDto
        {
            CategoryID =
                Convert.ToInt32(
                    reader["MenuItemCategoryID"]),

            CategoryName =
                reader["CategoryName"]?.ToString()
                ?? string.Empty,

            LandingPageMenuItemID =
                Convert.ToInt32(
                    reader["MenuItemID"]),

            MenuName =
                reader["MenuName"]?.ToString()
                ?? string.Empty,

            ScreenTitle =
                reader["ScreenTitle"]?.ToString()
                ?? string.Empty,

            Url =
                NormalizeInternalUrl(
                    reader["URL"]?.ToString()),

            IsConfiguredLandingPage =
                isConfiguredLandingPage,

            IsFallback =
                isFallback
        };
    }

    private static string NormalizeInternalUrl(
        string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "/access-denied";
        }

        var trimmed =
            url.Trim();

        return trimmed.StartsWith(
            "/",
            StringComparison.Ordinal)
                ? trimmed
                : $"/{trimmed}";
    }

    private sealed class CategoryLandingPageDto
    {
        public int CategoryID { get; set; }

        public string CategoryName { get; set; } =
            string.Empty;

        public int? LandingPageMenuItemID { get; set; }

        public string MenuName { get; set; } =
            string.Empty;

        public string ScreenTitle { get; set; } =
            string.Empty;

        public string Url { get; set; } = "/access-denied";

        public bool IsConfiguredLandingPage { get; set; }

        public bool IsFallback { get; set; }

        public static CategoryLandingPageDto Fallback(
            int categoryID,
            string categoryName)
        {
            return new CategoryLandingPageDto
            {
                CategoryID = categoryID,
                CategoryName = categoryName,
                Url = "/access-denied",
                IsFallback = true
            };
        }
    }
}
