using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Dashboard;

/// <summary>
/// GET /api/dashboard/currencytypes/{orgID} — all active currency types for the
/// organization, ordered alphabetically. Populates the Type filter checkbox list.
/// OrganizationCurrencyType stores its boolean flag as IsActive = 'true'.
/// </summary>
public class GetDashboardCurrencyTypes
{
    private readonly ILogger<GetDashboardCurrencyTypes> _logger;
    private readonly string _connectionString;

    public GetDashboardCurrencyTypes(ILogger<GetDashboardCurrencyTypes> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("GetDashboardCurrencyTypes")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dashboard/currencytypes/{orgID:int}")] HttpRequest req,
        int orgID)
    {
        _logger.LogInformation("GetDashboardCurrencyTypes triggered for org {OrgID}.", orgID);

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(
                """
                SELECT EMACurrencyTypeID, Description
                FROM   dbo.OrganizationCurrencyType
                WHERE  EMAOrganizationID = @OrgID
                  AND  IsActive = 'true'
                ORDER  BY Description;
                """,
                conn);
            cmd.Parameters.AddWithValue("@OrgID", orgID);

            var items = new List<object>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                items.Add(new { EMACurrencyTypeID = reader.GetInt32(0), Description = reader.GetString(1) });

            return new OkObjectResult(items);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching currency types for EMAOrganizationID {OrgID}", orgID);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }
}
