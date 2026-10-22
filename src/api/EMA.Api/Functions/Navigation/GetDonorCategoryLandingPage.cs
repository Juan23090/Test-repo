using System.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Navigation;

public sealed class GetDonorCategoryLandingPage
{
    private const int BrochureCategoryID = 10;
    private const int DonorsCategoryID = 30;

    private readonly ILogger<GetDonorCategoryLandingPage> _logger;
    private readonly string _connectionString;

    public GetDonorCategoryLandingPage(
        ILogger<GetDonorCategoryLandingPage> logger)
    {
        _logger = logger;

        _connectionString =
            Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString.");
    }

    [Function("GetDonorCategoryLandingPage")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "organization/{orgID:int}/donors/{donorID:int}/categories/{categoryID:int}/landing-page")]
        HttpRequest request,
        int orgID,
        int donorID,
        int categoryID)
    {
        _logger.LogInformation(
            "GetDonorCategoryLandingPage triggered for org {OrgID}, donor {DonorID}, category {CategoryID}.",
            orgID,
            donorID,
            categoryID);

        try
        {
            await using var connection =
                new SqlConnection(
                    _connectionString);

            await connection.OpenAsync();

            if (!await DonorBelongsToOrganizationAsync(
                    connection,
                    orgID,
                    donorID))
            {
                return new ObjectResult(new
                {
                    message =
                        "The donor does not belong to the selected organization."
                })
                {
                    StatusCode =
                        StatusCodes.Status403Forbidden
                };
            }

            if (!await DonorCanAccessCategoryAsync(
                    connection,
                    orgID,
                    categoryID))
            {
                return new ObjectResult(new
                {
                    message =
                        "The donor cannot access this menu category."
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
                    categoryID);

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
                "Error loading donor category landing page.");

            return new ObjectResult(new
            {
                message =
                    "Unable to load donor category landing page."
            })
            {
                StatusCode =
                    StatusCodes.Status500InternalServerError
            };
        }
    }

    private static async Task<bool> DonorBelongsToOrganizationAsync(
        SqlConnection connection,
        int orgID,
        int donorID)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM dbo.Donor
            WHERE EMADonorID = @DonorID
              AND EMAOrganizationID = @OrgID;
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters.Add(
            "@DonorID",
            SqlDbType.Int)
            .Value = donorID;

        command.Parameters.Add(
            "@OrgID",
            SqlDbType.Int)
            .Value = orgID;

        var count =
            Convert.ToInt32(
                await command.ExecuteScalarAsync());

        return count > 0;
    }

    private static async Task<bool> DonorCanAccessCategoryAsync(
        SqlConnection connection,
        int orgID,
        int categoryID)
    {
        if (categoryID == BrochureCategoryID)
        {
            return true;
        }

        if (categoryID != DonorsCategoryID)
        {
            return false;
        }

        const string sql = """
            SELECT COUNT(1)
            FROM dbo.Organization o
            INNER JOIN dbo.[Role] r
                ON o.DonorRoleID = r.RoleID
            INNER JOIN dbo.RoleMenuItem rmi
                ON r.RoleID = rmi.RoleID
            INNER JOIN dbo.MenuItem mi
                ON rmi.MenuItemID = mi.MenuItemID
            WHERE o.EMAOrganizationID = @OrgID
              AND mi.MenuItemCategoryID = @CategoryID
              AND mi.IsActive = 'Y'
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
            int categoryID)
    {
        var isBrochureRequest =
            categoryID == BrochureCategoryID;

        var sql =
            isBrochureRequest
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
                  FROM dbo.Organization o
                  INNER JOIN dbo.[Role] r
                      ON o.DonorRoleID = r.RoleID
                  INNER JOIN dbo.RoleMenuItem rmi
                      ON r.RoleID = rmi.RoleID
                  INNER JOIN dbo.MenuItem mi
                      ON rmi.MenuItemID = mi.MenuItemID
                  INNER JOIN dbo.MenuItemCategory mic
                      ON mi.MenuItemCategoryID = mic.MenuItemCategoryID
                  WHERE o.EMAOrganizationID = @OrgID
                    AND mic.MenuItemCategoryID = @CategoryID
                    AND mi.IsActive = 'Y'
                    AND UPPER(COALESCE(NULLIF(LTRIM(RTRIM(mi.DisplayControlID)), ''), 'M')) = 'L'
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

        if (!isBrochureRequest)
        {
            command.Parameters.Add(
                "@OrgID",
                SqlDbType.Int)
                .Value = orgID;
        }

        await using var reader =
            await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

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
                true,

            IsFallback =
                false
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
