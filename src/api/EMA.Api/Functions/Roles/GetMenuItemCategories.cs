using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace EMA.Api.Functions.Roles;

public class GetMenuItemCategories
{
    private readonly ILogger<GetMenuItemCategories> _logger;
    private readonly string _connectionString;

    public GetMenuItemCategories(ILogger<GetMenuItemCategories> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("GetMenuItemCategories")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "menu-item-categories")] HttpRequest req)
    {
        _logger.LogInformation("GetMenuItemCategories triggered.");

        var categories = new List<LookupItemDto>();

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = new SqlCommand("SELECT MenuItemCategoryID, Description FROM [dbo].[MenuItemCategory] ORDER BY MenuItemCategoryID", conn);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                categories.Add(new LookupItemDto
                {
                    ID = reader.GetInt16(0),
                    Description = reader.GetString(1)
                });
            }

            return new OkObjectResult(categories);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching menu item categories.");
            return new StatusCodeResult(500);
        }
    }

    public class LookupItemDto
    {
        public int ID { get; set; }
        public string Description { get; set; } = string.Empty;
    }
}

