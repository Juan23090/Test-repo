using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Dashboard;

/// <summary>
/// GET /api/dashboard/donor-names/{orgID}?q= — distinct Donation.InteracDonorName values for the
/// organization matching the query. This is the exact column RunReport's Name filter searches
/// (see RunReport.cs), so suggestions only ever include names that can actually return results.
/// </summary>
public class SearchDashboardDonorNames
{
    private readonly ILogger<SearchDashboardDonorNames> _logger;
    private readonly string _connectionString;

    public SearchDashboardDonorNames(ILogger<SearchDashboardDonorNames> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("SearchDashboardDonorNames")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dashboard/donor-names/{orgID:int}")] HttpRequest req,
        int orgID)
    {
        _logger.LogInformation("SearchDashboardDonorNames triggered for org {OrgID}.", orgID);

        try
        {
            string? query = req.Query["q"];

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var sql =
                """
                SELECT DISTINCT TOP 10 InteracDonorName
                FROM   dbo.Donation
                WHERE  EMAOrganizationID = @OrgID
                       AND InteracDonorName IS NOT NULL
                """;

            if (!string.IsNullOrWhiteSpace(query))
                sql += " AND InteracDonorName LIKE @Query";

            sql += " ORDER BY InteracDonorName;";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@OrgID", orgID);

            if (!string.IsNullOrWhiteSpace(query))
                cmd.Parameters.AddWithValue("@Query", $"%{query.Trim()}%");

            var names = new List<string>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                names.Add(reader.GetString(0));

            return new OkObjectResult(names);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching donor names for EMAOrganizationID {OrgID}", orgID);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }
}
