using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Dashboard;

/// <summary>
/// GET /api/savedqueries/{orgID} — all saved queries for the organization, ordered by name.
/// Each entry includes the serialised filter JSON so the client can restore all filter state.
/// </summary>
public class GetSavedQueries
{
    private readonly ILogger<GetSavedQueries> _logger;
    private readonly string _connectionString;

    public GetSavedQueries(ILogger<GetSavedQueries> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("GetSavedQueries")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "savedqueries/{orgID:int}")] HttpRequest req,
        int orgID)
    {
        _logger.LogInformation("GetSavedQueries triggered for org {OrgID}.", orgID);

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(
                """
                SELECT DashboardName, Filters, DateTimeCreated
                FROM   dbo.OrganizationDashboard
                WHERE  EMAOrganizationID = @OrgID
                ORDER  BY DashboardName;
                """,
                conn);
            cmd.Parameters.AddWithValue("@OrgID", orgID);

            var queries = new List<object>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                queries.Add(new
                {
                    Name            = reader.GetString(0),
                    Filters         = reader.GetString(1),
                    DateTimeCreated = reader.GetDateTime(2),
                });
            }

            return new OkObjectResult(queries);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching saved queries for EMAOrganizationID {OrgID}", orgID);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }
}
