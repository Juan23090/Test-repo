using EMA.Api.DTOs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace EMA_Api.Functions.Organization;

public class GetAccountingSoftware
{
    private readonly ILogger<GetAccountingSoftware> _logger;
    private readonly string _connectionString;

    public GetAccountingSoftware(ILogger<GetAccountingSoftware> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("GetAccountingSoftware")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "accounting-software")] HttpRequest req)
    {
        _logger.LogInformation("GetAccountingSoftware triggered.");

        var results = new List<AccountingSoftwareDTO>();

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(
                @"SELECT AccountingSoftwareID,
                         [Name],
                         MarketingURL,
                         JESpecURL
                  FROM dbo.AccountingSoftware
                  ORDER BY [Name]",
                conn);

            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                results.Add(new AccountingSoftwareDTO
                {
                    AccountingSoftwareID = reader.GetInt32(reader.GetOrdinal("AccountingSoftwareID")),
                    Name = reader.GetString(reader.GetOrdinal("Name")),
                    MarketingURL = reader.IsDBNull(reader.GetOrdinal("MarketingURL"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("MarketingURL")),
                    JESpecURL = reader.IsDBNull(reader.GetOrdinal("JESpecURL"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("JESpecURL"))
                });
            }

            return new OkObjectResult(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching accounting software.");
            return new StatusCodeResult(500);
        }
    }
}