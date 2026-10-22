using EMA.Api.DTOs;
using EMA.Api.Services.Navigation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Roles;

public class GetMenuItems
{
    private readonly ILogger<GetMenuItems> _logger;
    private readonly string _connectionString;

    public GetMenuItems(ILogger<GetMenuItems> logger)
    {
        _logger = logger;
        _connectionString =
            Environment.GetEnvironmentVariable("SqlConnectionString")
            ?? throw new InvalidOperationException("Missing SqlConnectionString.");
    }

    [Function("GetMenuItems")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "menu-items")]
        HttpRequest request)
    {
        _logger.LogInformation("GetMenuItems triggered.");

        var menuItems = new List<MenuItemDto>();
        var roleMappings = new Dictionary<int, List<int>>();

        try
        {
            await using var connection =
                new SqlConnection(_connectionString);

            await connection.OpenAsync(
                request.HttpContext.RequestAborted);

            await using (var roleCommand =
                new SqlCommand(
                    "SELECT MenuItemID, RoleID FROM dbo.RoleMenuItem",
                    connection))
            await using (var roleReader =
                await roleCommand.ExecuteReaderAsync(
                    request.HttpContext.RequestAborted))
            {
                while (await roleReader.ReadAsync(
                           request.HttpContext.RequestAborted))
                {
                    var menuItemID = roleReader.GetInt32(0);
                    var roleID = roleReader.GetInt32(1);

                    if (!roleMappings.TryGetValue(
                            menuItemID,
                            out var roleList))
                    {
                        roleList = new List<int>();
                        roleMappings[menuItemID] = roleList;
                    }

                    roleList.Add(roleID);
                }
            }

            const string sql = """
                SELECT
                    MenuItemID,
                    MenuItemCategoryID,
                    MenuItemRowID,
                    MenuItemTypeID,
                    MenuName,
                    ScreenTitle,
                    DisplayControlID,
                    URL,
                    IsActive
                FROM dbo.MenuItem
                ORDER BY MenuItemID;
                """;

            await using (var menuCommand =
                new SqlCommand(sql, connection))
            await using (var menuReader =
                await menuCommand.ExecuteReaderAsync(
                    request.HttpContext.RequestAborted))
            {
                while (await menuReader.ReadAsync(
                           request.HttpContext.RequestAborted))
                {
                    var menuItemID =
                        Convert.ToInt32(menuReader["MenuItemID"]);

                    roleMappings.TryGetValue(
                        menuItemID,
                        out var roles);

                    menuItems.Add(new MenuItemDto
                    {
                        MenuItemID = menuItemID,
                        MenuItemCategoryID =
                            menuReader["MenuItemCategoryID"] is DBNull
                                ? null
                                : Convert.ToInt32(
                                    menuReader["MenuItemCategoryID"]),
                        Row =
                            menuReader["MenuItemRowID"] is DBNull
                                ? null
                                : Convert.ToInt32(
                                    menuReader["MenuItemRowID"]),
                        MenuItemTypeID =
                            Convert.ToInt32(
                                menuReader["MenuItemTypeID"]),
                        MenuName =
                            menuReader["MenuName"]?.ToString()
                            ?? string.Empty,
                        ScreenTitle =
                            menuReader["ScreenTitle"]?.ToString()
                            ?? string.Empty,
                        DisplayControlID =
                            NormalizeDisplayControlID(
                                menuReader["DisplayControlID"] is DBNull
                                    ? null
                                    : menuReader["DisplayControlID"]?.ToString()),
                        URL =
                            menuReader["URL"] is DBNull
                                ? null
                                : menuReader["URL"]?.ToString(),
                        IsActive =
                            menuReader["IsActive"] is DBNull
                                ? null
                                : menuReader["IsActive"]?.ToString(),
                        RoleIDs = roles ?? new List<int>()
                    });
                }
            }

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

            return new OkObjectResult(menuItems);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error fetching menu items.");

            return new StatusCodeResult(
                StatusCodes.Status500InternalServerError);
        }
    }

    private static string NormalizeDisplayControlID(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        return normalized is "M" or "W" or "B" or "L"
            ? normalized
            : "M";
    }

    public sealed class MenuItemDto
    {
        public int MenuItemID { get; set; }
        public int? MenuItemCategoryID { get; set; }
        public int? Row { get; set; }
        public int MenuItemTypeID { get; set; }
        public string DisplayControlID { get; set; } = "M";
        public string MenuName { get; set; } = string.Empty;
        public string ScreenTitle { get; set; } = string.Empty;
        public string? URL { get; set; }
        public string? IsActive { get; set; }
        public List<int> RoleIDs { get; set; } = new();
        public List<MenuItemResourceDto> Resources { get; set; } = new();
    }
}
