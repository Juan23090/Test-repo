using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Dashboard;

/// <summary>
/// DELETE /api/savedqueries/{orgID}/{name} — deletes a named saved query for the organization.
/// Returns 404 if no matching row is found so the client can surface a clear error.
/// </summary>
public class DeleteSavedQuery
{
    private readonly ILogger<DeleteSavedQuery> _logger;
    private readonly string _connectionString;

    public DeleteSavedQuery(ILogger<DeleteSavedQuery> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("DeleteSavedQuery")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "savedqueries/{orgID:int}/{name}")] HttpRequest req,
        int orgID,
        string name)
    {
        _logger.LogInformation("DeleteSavedQuery triggered for org {OrgID} and query '{Name}'.", orgID, name);

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(
                "DELETE FROM dbo.OrganizationDashboard WHERE EMAOrganizationID = @OrgID AND DashboardName = @Name;",
                conn);
            cmd.Parameters.AddWithValue("@OrgID", orgID);
            cmd.Parameters.AddWithValue("@Name",  name);

            int affected = await cmd.ExecuteNonQueryAsync();
            if (affected == 0)
                return new NotFoundObjectResult(new { Error = $"Query '{name}' not found." });

            return new OkObjectResult(new { Deleted = name });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting query '{Name}' for EMAOrganizationID {OrgID}", name, orgID);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }
}
