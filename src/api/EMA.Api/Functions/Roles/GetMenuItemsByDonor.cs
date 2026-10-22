using EMA.Api.DTOs;
using EMA.Api.Services.Navigation;
using System.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Roles;

public sealed class GetMenuItemsByDonor
{
    private const int BrochureCategoryID = 10;
    private const int DonorsCategoryID = 30;

    private readonly ILogger<GetMenuItemsByDonor> _logger;
    private readonly string _connectionString;

    public GetMenuItemsByDonor(
        ILogger<GetMenuItemsByDonor> logger)
    {
        _logger = logger;

        _connectionString =
            Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString.");
    }

    [Function("GetMenuItemsByDonor")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,             "get",
            Route = "organization/{orgID:int}/donors/{donorID:int}/menu-items")]
        HttpRequest request,
        int orgID,
        int donorID)
    {
        int? categoryID = null;

        if (request.Query.TryGetValue(
                "categoryID",
                out var categoryValue)
            &&
            int.TryParse(
                categoryValue,
                out var parsedCategoryID))
        {
            categoryID = parsedCategoryID;
        }

        _logger.LogInformation(
            "GetMenuItemsByDonor triggered for org {OrgID}, donor {DonorID}, category {CategoryID}.",
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

            var isBrochureRequest =
                categoryID == BrochureCategoryID;

            if (categoryID.HasValue &&
                categoryID.Value != BrochureCategoryID &&
                categoryID.Value != DonorsCategoryID)
            {
                return new OkObjectResult(
                    Array.Empty<MenuItemDto>());
            }

            var sql =
                isBrochureRequest
                    ? """
                      SELECT
                          mi.MenuItemID,
                          mi.MenuItemCategoryID,
                          mi.MenuItemRowID,
                          mi.MenuItemTypeID,
                          mi.MenuName,
                          mi.ScreenTitle,
                          mi.URL,
                          mi.IsActive
                      FROM dbo.MenuItem mi
                      WHERE mi.MenuItemCategoryID = @CategoryID
                        AND mi.IsActive = 'Y'
                        AND UPPER(COALESCE(NULLIF(LTRIM(RTRIM(mi.DisplayControlID)), ''), 'M')) IN ('M', 'B')
                      ORDER BY
                          mi.MenuItemCategoryID,
                          mi.MenuItemRowID,
                          mi.MenuItemID;
                      """
                    : """
                      SELECT DISTINCT
                          mi.MenuItemID,
                          mi.MenuItemCategoryID,
                          mi.MenuItemRowID,
                          mi.MenuItemTypeID,
                          mi.MenuName,
                          mi.ScreenTitle,
                          mi.URL,
                          mi.IsActive
                      FROM dbo.Organization o
                      INNER JOIN dbo.[Role] r
                          ON o.DonorRoleID =
                             r.RoleID
                      INNER JOIN dbo.RoleMenuItem rmi
                          ON r.RoleID =
                             rmi.RoleID
                      INNER JOIN dbo.MenuItem mi
                          ON rmi.MenuItemID =
                             mi.MenuItemID
                      WHERE o.EMAOrganizationID = @OrgID
                        AND r.IsActive = 'Y'
                        AND mi.IsActive = 'Y'
                        AND UPPER(COALESCE(NULLIF(LTRIM(RTRIM(mi.DisplayControlID)), ''), 'M')) IN ('M', 'B')
                        AND mi.MenuItemCategoryID = @DonorsCategoryID
                        AND
                        (
                            @CategoryID IS NULL
                            OR
                            mi.MenuItemCategoryID =
                                @CategoryID
                        )
                      ORDER BY
                          mi.MenuItemCategoryID,
                          mi.MenuItemRowID,
                          mi.MenuItemID;
                      """;

            await using var command =
                new SqlCommand(
                    sql,
                    connection);

            var categoryParameter =
                command.Parameters.Add(
                    "@CategoryID",
                    SqlDbType.Int);

            categoryParameter.Value =
                categoryID.HasValue
                    ? categoryID.Value
                    : DBNull.Value;

            if (!isBrochureRequest)
            {
                command.Parameters.Add(
                    "@OrgID",
                    SqlDbType.Int)
                    .Value = orgID;

                command.Parameters.Add(
                    "@DonorsCategoryID",
                    SqlDbType.Int)
                    .Value = DonorsCategoryID;
            }

            var menuItems =
                new List<MenuItemDto>();

            await using var reader =
                await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                menuItems.Add(
                    new MenuItemDto
                    {
                        MenuItemID =
                            Convert.ToInt32(
                                reader["MenuItemID"]),

                        MenuItemCategoryID =
                            reader["MenuItemCategoryID"]
                                is DBNull
                                ? null
                                : Convert.ToInt32(
                                    reader["MenuItemCategoryID"]),

                        Row =
                            reader["MenuItemRowID"]
                                is DBNull
                                ? null
                                : Convert.ToInt32(
                                    reader["MenuItemRowID"]),

                        MenuItemTypeID =
                            Convert.ToInt32(
                                reader["MenuItemTypeID"]),

                        MenuName =
                            reader["MenuName"]?.ToString()
                            ?? string.Empty,

                        ScreenTitle =
                            reader["ScreenTitle"]?.ToString()
                            ?? string.Empty,

                        URL =
                            reader["URL"] is DBNull
                                ? null
                                : reader["URL"]?.ToString(),

                        IsActive =
                            reader["IsActive"] is DBNull
                                ? null
                                : reader["IsActive"]?.ToString()
                    });
            }

            await reader.DisposeAsync();

            var resourcesByMenuItem =
                await MenuItemResourceQuery.LoadAsync(
                    connection,
                    menuItems.Select(item => item.MenuItemID),
                    request.HttpContext.RequestAborted);

            foreach (var menuItem in menuItems)
            {
                if (resourcesByMenuItem.TryGetValue(
                        menuItem.MenuItemID,
                        out var resources))
                {
                    menuItem.Resources = resources;
                }
            }

            return new OkObjectResult(
                menuItems);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error fetching donor menu items for donor {DonorID}, organization {OrgID}, category {CategoryID}.",
                donorID,
                orgID,
                categoryID);

            return new ObjectResult(new
            {
                message =
                    "Unable to load donor menu items."
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

    public sealed class MenuItemDto
    {
        public int MenuItemID { get; set; }

        public int? MenuItemCategoryID { get; set; }

        public int? Row { get; set; }

        public int MenuItemTypeID { get; set; }

        public string MenuName { get; set; } =
            string.Empty;

        public string ScreenTitle { get; set; } =
            string.Empty;

        public string? URL { get; set; }

        public List<MenuItemResourceDto> Resources { get; set; } = new();

        public string? IsActive { get; set; }
    }
}
