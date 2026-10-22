using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace EMA.Api.Functions.Roles;

public class GetMenuItemRows
{
    private readonly ILogger<GetMenuItemRows> _logger;
    private readonly string _connectionString;

    public GetMenuItemRows(ILogger<GetMenuItemRows> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("GetMenuItemRows")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "menu-item-rows")] HttpRequest req)
    {
        _logger.LogInformation("GetMenuItemRows triggered.");

        var rows = new List<LookupItemDto>();

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = new SqlCommand("SELECT MenuItemRowID, Description FROM [dbo].[MenuItemRow] ORDER BY MenuItemRowID", conn);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(new LookupItemDto
                {
                    ID = reader.GetInt16(0),
                    Description = reader.IsDBNull(1) ? string.Empty : reader.GetString(1)
                });
            }

            return new OkObjectResult(rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching menu item rows.");
            return new StatusCodeResult(500);
        }
    }

    public class LookupItemDto
    {
        public int ID { get; set; }
        public string Description { get; set; } = string.Empty;
    }
}

