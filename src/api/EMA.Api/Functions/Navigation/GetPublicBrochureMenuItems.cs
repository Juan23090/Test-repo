using System.Data;
using EMA.Api.DTOs;
using EMA.Api.Services.Navigation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Navigation;

/// <summary>
/// Returns only the active public brochure navigation items.
/// This endpoint deliberately does not accept organization or user identifiers.
/// </summary>
public sealed class GetPublicBrochureMenuItems
{
    private const int BrochureCategoryID = 10;

    private readonly ILogger<GetPublicBrochureMenuItems> _logger;
    private readonly string _connectionString;

    public GetPublicBrochureMenuItems(
        ILogger<GetPublicBrochureMenuItems> logger)
    {
        _logger = logger;
        _connectionString =
            Environment.GetEnvironmentVariable("SqlConnectionString")
            ?? throw new InvalidOperationException("Missing SqlConnectionString.");
    }

    [Function("GetPublicBrochureMenuItems")]
    public async Task<IActionResult> RunAsync(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "navigation/public-menu-items")]
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "GetPublicBrochureMenuItems triggered.");

        const string sql = """
            SELECT
                MenuItemID,
                MenuItemCategoryID,
                MenuItemRowID,
                MenuItemTypeID,
                MenuName,
                ScreenTitle,
                URL,
                IsActive
            FROM dbo.MenuItem
            WHERE MenuItemCategoryID = @CategoryID
              AND IsActive = 'Y'
              AND UPPER(COALESCE(NULLIF(LTRIM(RTRIM(DisplayControlID)), ''), 'M')) IN ('M', 'B')
            ORDER BY
                MenuItemRowID,
                MenuItemID;
            """;

        try
        {
            await using var connection =
                new SqlConnection(_connectionString);

            await connection.OpenAsync(cancellationToken);

            await using var command =
                new SqlCommand(sql, connection);

            command.Parameters.Add(
                "@CategoryID",
                SqlDbType.Int)
                .Value = BrochureCategoryID;

            var menuItems =
                new List<PublicMenuItemDto>();

            await using (var reader =
                await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    menuItems.Add(new PublicMenuItemDto
                    {
                        MenuItemID =
                            Convert.ToInt32(reader["MenuItemID"]),
                        MenuItemCategoryID =
                            reader["MenuItemCategoryID"] is DBNull
                                ? null
                                : Convert.ToInt32(
                                    reader["MenuItemCategoryID"]),
                        Row =
                            reader["MenuItemRowID"] is DBNull
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
            }

            var resourcesByMenuItem =
                await MenuItemResourceQuery.LoadAsync(
                    connection,
                    menuItems.Select(item => item.MenuItemID),
                    cancellationToken);

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
                "Unable to load public brochure menu items.");

            return new ObjectResult(new
            {
                message =
                    "Unable to load the public navigation menu."
            })
            {
                StatusCode =
                    StatusCodes.Status500InternalServerError
            };
        }
    }

    public sealed class PublicMenuItemDto
    {
        public int MenuItemID { get; set; }
        public int? MenuItemCategoryID { get; set; }
        public int? Row { get; set; }
        public int MenuItemTypeID { get; set; }
        public string MenuName { get; set; } = string.Empty;
        public string ScreenTitle { get; set; } = string.Empty;
        public string? URL { get; set; }
        public string? IsActive { get; set; }
        public List<MenuItemResourceDto> Resources { get; set; } = new();
    }
}
