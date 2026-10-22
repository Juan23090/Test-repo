using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Dashboard;

/// <summary>
/// GET /api/dashboard/methods/{orgID} — all payment methods for the organization,
/// ordered alphabetically. Populates the Methods filter checkbox list.
/// </summary>
public class GetDashboardMethods
{
    private readonly ILogger<GetDashboardMethods> _logger;
    private readonly string _connectionString;

    public GetDashboardMethods(ILogger<GetDashboardMethods> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("GetDashboardMethods")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dashboard/methods/{orgID:int}")] HttpRequest req,
        int orgID)
    {
        _logger.LogInformation("GetDashboardMethods triggered for org {OrgID}.", orgID);

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(
                """
                SELECT EMAMethodOfGivingID, Description
                FROM   dbo.OrganizationMethodOfGiving
                WHERE  EMAOrganizationID = @OrgID
                ORDER  BY Description;
                """,
                conn);
            cmd.Parameters.AddWithValue("@OrgID", orgID);

            var items = new List<object>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                items.Add(new { EMAMethodOfGivingID = reader.GetInt32(0), Description = reader.GetString(1) });

            return new OkObjectResult(items);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching methods for EMAOrganizationID {OrgID}", orgID);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }
}
