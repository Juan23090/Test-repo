using EMA.Api.DTOs;
using EMA.Api.Services.Navigation;
using EMA.Api.Services.Security;
using System.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Roles;

public class GetMenuItemsByUser
{
    private const int BrochureCategoryID = 10;

    private readonly ILogger<GetMenuItemsByUser> _logger;
    private readonly ApiRequestBoundaryValidator _authorization;
    private readonly string _connectionString;

    public GetMenuItemsByUser(
        ILogger<GetMenuItemsByUser> logger,
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

    [Function("GetMenuItemsByUser")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,             "get",
            Route =
                "organization/{orgID:int}/users/{userID:int}/menu-items")]
        HttpRequest request,
        int orgID,
        int userID)
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

        var identity =
            request.GetRequiredApiIdentity();

        var isAdministrator =
            _authorization.IsOrganizationAdministrator(
                identity);

        _logger.LogInformation(
            "GetMenuItemsByUser triggered for org {OrgID}, user {UserID}, category {CategoryID}.",
            orgID,
            userID,
            categoryID);

        try
        {
            await using var connection =
                new SqlConnection(
                    _connectionString);

            await connection.OpenAsync();

            var isBrochureRequest =
                categoryID ==
                BrochureCategoryID;

            var sql =
                isBrochureRequest
                    ? """
                      SELECT
                          mi.MenuItemID,
                          mi.MenuItemCategoryID,
                          mi.MenuItemRowID,
                          mi.MenuItemTypeID,
                          mi.DisplayControlID,
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
                    : isAdministrator
                        ? """
                          SELECT
                              mi.MenuItemID,
                              mi.MenuItemCategoryID,
                              mi.MenuItemRowID,
                              mi.MenuItemTypeID,
                              mi.DisplayControlID,
                              mi.MenuName,
                              mi.ScreenTitle,
                              mi.URL,
                              mi.IsActive
                          FROM dbo.MenuItem mi
                          WHERE
                          (
                              @CategoryID IS NULL
                              OR
                              mi.MenuItemCategoryID =
                                  @CategoryID
                          )
                            AND mi.IsActive = 'Y'
                            AND UPPER(COALESCE(NULLIF(LTRIM(RTRIM(mi.DisplayControlID)), ''), 'M')) IN ('M', 'B', 'W')
                            AND
                            (
                                mi.MenuItemCategoryID = @BrochureCategoryID
                                OR EXISTS
                                (
                                    SELECT 1
                                    FROM dbo.OrganizationUserRole ur
                                    INNER JOIN dbo.RoleMenuItem rmi
                                        ON ur.RoleID = rmi.RoleID
                                    INNER JOIN dbo.MenuItem mappedMenuItem
                                        ON rmi.MenuItemID =
                                           mappedMenuItem.MenuItemID
                                    INNER JOIN dbo.OrganizationUser ou
                                        ON ur.UserID = ou.UserID
                                       AND ou.EMAOrganizationID =
                                           @OrgID
                                    INNER JOIN dbo.[User] u
                                        ON ur.UserID = u.UserID
                                    INNER JOIN dbo.[Role] r
                                        ON ur.RoleID = r.RoleID
                                    WHERE ur.EMAOrganizationID = @OrgID
                                      AND ur.UserID = @UserID
                                      AND mappedMenuItem.MenuItemCategoryID =
                                          mi.MenuItemCategoryID
                                      AND mappedMenuItem.IsActive = 'Y'
                                      AND u.IsActive = 1
                                      AND r.IsActive = 'Y'
                                )
                            )
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
                              mi.DisplayControlID,
                              mi.MenuName,
                              mi.ScreenTitle,
                              mi.URL,
                              mi.IsActive
                          FROM dbo.MenuItem mi
                          INNER JOIN dbo.RoleMenuItem rmi
                              ON mi.MenuItemID =
                                 rmi.MenuItemID
                          INNER JOIN dbo.OrganizationUserRole ur
                              ON rmi.RoleID =
                                 ur.RoleID
                             AND ur.EMAOrganizationID =
                                 @OrgID
                          INNER JOIN dbo.OrganizationUser ou
                              ON ur.UserID =
                                 ou.UserID
                          INNER JOIN dbo.[User] u
                              ON ur.UserID =
                                 u.UserID
                          INNER JOIN dbo.[Role] r
                              ON ur.RoleID =
                                 r.RoleID
                          WHERE ou.EMAOrganizationID = @OrgID
                            AND ur.UserID = @UserID
                            AND
                            (
                                @CategoryID IS NULL
                                OR
                                mi.MenuItemCategoryID =
                                    @CategoryID
                            )
                            AND mi.IsActive = 'Y'
                            AND UPPER(COALESCE(NULLIF(LTRIM(RTRIM(mi.DisplayControlID)), ''), 'M')) IN ('M', 'B', 'W')
                            AND u.IsActive = 1
                            AND r.IsActive = 'Y'
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
                    "@UserID",
                    SqlDbType.Int)
                    .Value = userID;

                if (isAdministrator)
                {
                    command.Parameters.Add(
                        "@BrochureCategoryID",
                        SqlDbType.Int)
                        .Value = BrochureCategoryID;
                }
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
                                reader[
                                    "MenuItemID"]),

                        MenuItemCategoryID =
                            reader[
                                "MenuItemCategoryID"]
                                is DBNull
                                ? null
                                : Convert.ToInt32(
                                    reader[
                                        "MenuItemCategoryID"]),

                        Row =
                            reader[
                                "MenuItemRowID"]
                                is DBNull
                                ? null
                                : Convert.ToInt32(
                                    reader[
                                        "MenuItemRowID"]),

                        MenuItemTypeID =
                            Convert.ToInt32(
                                reader[
                                    "MenuItemTypeID"]),

                        DisplayControlID =
                            NormalizeDisplayControlID(
                                reader["DisplayControlID"] is DBNull
                                    ? null
                                    : reader["DisplayControlID"]?.ToString()),

                        MenuName =
                            reader[
                                "MenuName"]
                                ?.ToString()
                            ?? string.Empty,

                        ScreenTitle =
                            reader[
                                "ScreenTitle"]
                                ?.ToString()
                            ?? string.Empty,

                        URL =
                            reader["URL"]
                                is DBNull
                                ? null
                                : reader["URL"]
                                    ?.ToString(),

                        IsActive =
                            reader[
                                "IsActive"]
                                is DBNull
                                ? null
                                : reader[
                                    "IsActive"]
                                    ?.ToString()
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
                "Error fetching menu items for user {UserID}, organization {OrgID}, category {CategoryID}.",
                userID,
                orgID,
                categoryID);

            return new ObjectResult(
                new
                {
                    message =
                        "Unable to load menu items."
                })
            {
                StatusCode =
                    StatusCodes
                        .Status500InternalServerError
            };
        }
    }

    private static string NormalizeDisplayControlID(
        string? value)
    {
        var normalized =
            value?.Trim().ToUpperInvariant();

        return normalized is "M" or "W" or "B" or "L"
            ? normalized
            : "M";
    }

    public sealed class MenuItemDto
    {
        public int MenuItemID { get; set; }

        public int? MenuItemCategoryID
        {
            get;
            set;
        }

        public int? Row { get; set; }

        public int MenuItemTypeID
        {
            get;
            set;
        }

        public string DisplayControlID
        {
            get;
            set;
        } = "M";

        public string MenuName
        {
            get;
            set;
        } = string.Empty;

        public string ScreenTitle
        {
            get;
            set;
        } = string.Empty;

        public string? URL { get; set; }

        public List<MenuItemResourceDto> Resources { get; set; } = new();

        public string? IsActive
        {
            get;
            set;
        }
    }
}
