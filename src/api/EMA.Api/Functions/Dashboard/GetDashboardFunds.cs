using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Dashboard;

/// <summary>
/// GET /api/dashboard/funds/{orgID} — all funds for the organization, ordered
/// alphabetically. Populates the Fund filter checkbox list.
/// </summary>
public class GetDashboardFunds
{
    private readonly ILogger<GetDashboardFunds> _logger;
    private readonly string _connectionString;

    public GetDashboardFunds(ILogger<GetDashboardFunds> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("GetDashboardFunds")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dashboard/funds/{orgID:int}")] HttpRequest req,
        int orgID)
    {
        _logger.LogInformation("GetDashboardFunds triggered for org {OrgID}.", orgID);

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(
                """
                SELECT EMAFundID, Name
                FROM   dbo.OrganizationFund
                WHERE  EMAOrganizationID = @OrgID
                ORDER  BY Name;
                """,
                conn);
            cmd.Parameters.AddWithValue("@OrgID", orgID);

            var items = new List<object>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                items.Add(new { EMAFundID = reader.GetInt32(0), Name = reader.GetString(1) });

            return new OkObjectResult(items);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching funds for EMAOrganizationID {OrgID}", orgID);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }
}
