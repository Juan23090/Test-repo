using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace EMA_Api.Functions.Customization;

public class GetFunds
{
    private readonly ILogger<GetFunds> _logger;
    private readonly string _connectionString;

    public GetFunds(ILogger<GetFunds> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    private static readonly HashSet<string> CharBoolColumns = new() { "FundActive", "TaxDeductible" };

    [Function("GetFunds")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "customization/funds/{orgID:int}")] HttpRequest req,
        int orgID)
    {
        // TODO: replace orgID param with auth claim when authentication is implemented
        _logger.LogInformation("GetFunds triggered for org {OrgID}.", orgID);
        var activeOnly = req.Query.ContainsKey("activeOnly") && req.Query["activeOnly"] == "true";
        var results = new List<Dictionary<string, object?>>();
        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = new SqlCommand(
                @"SELECT EMAOrganizationID, EMAFundID,
                         ChMSFundID, Sequence,
                         Name AS FundName,
                         GLAccountNumber,
                         TaxDeductible,
                         EMACampusID AS EMACampusID,
                         IsActive AS FundActive,
                         DateTimeCreated, CreatedByUserID,
                         DateTimeUpdated, UpdatedbyUserID
                  FROM OrganizationFund
                  WHERE EMAOrganizationID = @OrgID
                  AND (@ActiveOnly = 0 OR IsActive = 'Y')
                  ORDER BY Sequence",
                conn);
            cmd.Parameters.AddWithValue("@OrgID", orgID);
            cmd.Parameters.AddWithValue("@ActiveOnly", activeOnly ? 1 : 0);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var col = reader.GetName(i);
                    var val = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    row[col] = CharBoolColumns.Contains(col) && val != null
                        ? val.ToString()!.Trim().Equals("Y", StringComparison.OrdinalIgnoreCase)
                        : val;
                }
                results.Add(row);
            }
            return new OkObjectResult(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching funds.");
            return new StatusCodeResult(500);
        }
    }
}
