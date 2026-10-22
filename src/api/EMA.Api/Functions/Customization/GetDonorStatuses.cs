using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace EMA_Api.Functions.Customization;

public class GetDonorStatuses
{
    private readonly ILogger<GetDonorStatuses> _logger;
    private readonly string _connectionString;

    public GetDonorStatuses(ILogger<GetDonorStatuses> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("GetDonorStatuses")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "customization/donor-statuses/{orgID:int}")] HttpRequest req,
        int orgID)
    {
        _logger.LogInformation("GetDonorStatuses triggered for org {OrgID}.", orgID);
        var activeOnly = req.Query.ContainsKey("activeOnly") && req.Query["activeOnly"] == "true";
        var results = new List<Dictionary<string, object?>>();
        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            // Description is nchar (fixed-length, space-padded) — RTRIM so the UI isn't padded.
            // IsActive is a real bit, so it comes back as a bool already (no Y/N conversion).
            using var cmd = new SqlCommand(
                @"SELECT EMAOrganizationId, DonorStatusID, Sequence,
                         RTRIM(Description) AS Description, IsActive
                  FROM DonorStatus
                  WHERE EMAOrganizationId = @OrgID
                  AND (@ActiveOnly = 0 OR IsActive = 1)
                  ORDER BY Sequence",
                conn);
            cmd.Parameters.AddWithValue("@OrgID", orgID);
            cmd.Parameters.AddWithValue("@ActiveOnly", activeOnly ? 1 : 0);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                results.Add(row);
            }
            return new OkObjectResult(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching donor statuses.");
            return new StatusCodeResult(500);
        }
    }
}
