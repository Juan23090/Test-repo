using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace EMA_Api.Functions.Customization;

public class GetEntityAliases
{
    private readonly ILogger<GetEntityAliases> _logger;
    private readonly string _connectionString;

    public GetEntityAliases(ILogger<GetEntityAliases> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("GetEntityAliases")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "customization/entity-aliases/{orgID:int}")] HttpRequest req,
        int orgID)
    {
        _logger.LogInformation("GetEntityAliases triggered for org {OrgID}.", orgID);
        var results = new List<Dictionary<string, object?>>();
        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = new SqlCommand(
                @"SELECT DISTINCT c.EntityName, oa.AliasName
                  FROM ChMSEntityAlias c
                  LEFT JOIN OrganizationEntityAliase oa
                      ON oa.EntityName = c.EntityName AND oa.EMAOrganizationID = @OrgID
                  ORDER BY c.EntityName",
                conn);
            cmd.Parameters.AddWithValue("@OrgID", orgID);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var col = reader.GetName(i);
                    row[col] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
                results.Add(row);
            }
            return new OkObjectResult(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching entity aliases.");
            return new StatusCodeResult(500);
        }
    }
}
