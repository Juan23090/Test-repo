using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace EMA.Api.Functions.Roles;

public class GetMenuItemTypes
{
    private readonly ILogger<GetMenuItemTypes> _logger;
    private readonly string _connectionString;

    public GetMenuItemTypes(ILogger<GetMenuItemTypes> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("GetMenuItemTypes")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "menu-item-types")] HttpRequest req)
    {
        _logger.LogInformation("GetMenuItemTypes triggered.");

        var types = new List<LookupItemDto>();

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = new SqlCommand("SELECT MenuItemTypeID, Description FROM [dbo].[MenuItemType] ORDER BY MenuItemTypeID", conn);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                types.Add(new LookupItemDto
                {
                    ID = reader.GetInt16(0),
                    Description = reader.GetString(1)
                });
            }

            return new OkObjectResult(types);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching menu item types.");
            return new StatusCodeResult(500);
        }
    }

    public class LookupItemDto
    {
        public int ID { get; set; }
        public string Description { get; set; } = string.Empty;
    }
}

