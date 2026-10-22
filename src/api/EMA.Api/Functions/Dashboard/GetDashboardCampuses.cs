using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Dashboard;

/// <summary>
/// GET /api/dashboard/campuses/{orgID} — all active campuses for the organization,
/// ordered alphabetically. Populates the Campuses filter checkbox list.
/// OrganizationCampus stores its boolean flag as IsActive = 'true' (confirmed live schema).
/// </summary>
public class GetDashboardCampuses
{
    private readonly ILogger<GetDashboardCampuses> _logger;
    private readonly string _connectionString;

    public GetDashboardCampuses(ILogger<GetDashboardCampuses> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("GetDashboardCampuses")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dashboard/campuses/{orgID:int}")] HttpRequest req,
        int orgID)
    {
        _logger.LogInformation("GetDashboardCampuses triggered for org {OrgID}.", orgID);

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(
                """
                SELECT EMACampusID, Name
                FROM   dbo.OrganizationCampus
                WHERE  EMAOrganizationID = @OrgID
                  AND  IsActive = 'true'
                ORDER  BY Name;
                """,
                conn);
            cmd.Parameters.AddWithValue("@OrgID", orgID);

            var items = new List<object>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                items.Add(new { EMACampusID = reader.GetInt32(0), Name = reader.GetString(1) });

            return new OkObjectResult(items);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching campuses for EMAOrganizationID {OrgID}", orgID);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }
}
