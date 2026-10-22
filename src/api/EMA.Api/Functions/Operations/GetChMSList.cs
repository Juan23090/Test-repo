using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace EMA_Api.Functions.Operations;

public class GetChMSList
{
    private readonly ILogger<GetChMSList> _logger;
    private readonly string _connectionString;

    public GetChMSList(ILogger<GetChMSList> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("GetChMSList")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "savings-calculator/chms-options")] HttpRequest req)
    {
        _logger.LogInformation("GetChMSList triggered.");
        var results = new List<Dictionary<string, object?>>();
        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = new SqlCommand(
                @"SELECT ChMSID, ChMSName
                  FROM ChMS
                  ORDER BY ChMSName",
                conn);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>
                {
                    { "ChMSID", reader.GetInt32(0) },
                    { "ChMSName", reader.IsDBNull(1) ? null : reader.GetString(1) }
                };
                results.Add(row);
            }
            return new OkObjectResult(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching ChMS list.");
            return new StatusCodeResult(500);
        }
    }
}

