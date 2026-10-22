using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace EMA_Api.Functions.Organization;

/// <summary>
/// GET /api/chms/lookups — the option lists for the ChMS admin dropdowns.
/// Each lookup table is single-column (a char(30) PK that IS the display text), so we just
/// return the trimmed string lists: CandidateFor, CandidateRating, ApiRating.
/// </summary>
public class GetChMSLookups
{
    private readonly ILogger<GetChMSLookups> _logger;
    private readonly string _connectionString;

    public GetChMSLookups(ILogger<GetChMSLookups> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("GetChMSLookups")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "chms/lookups")] HttpRequest req)
    {
        _logger.LogInformation("GetChMSLookups triggered.");

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var candidateFor    = await ReadColumnAsync(conn, "dbo.ChMSCandidateForEMA", "ChMSCandidateForEMAID");
            var candidateRating = await ReadColumnAsync(conn, "dbo.ChMSCandidateRating", "ChMSCandidateRatingID");
            var apiRating       = await ReadColumnAsync(conn, "dbo.ChMSAPIRating",       "ChMSAPIRatingID");

            return new OkObjectResult(new
            {
                CandidateFor    = candidateFor,
                CandidateRating = candidateRating,
                ApiRating       = apiRating
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching ChMS lookups.");
            return new StatusCodeResult(500);
        }
    }

    // Reads a single-column lookup table, trimming the space-padded char(30) values.
    private static async Task<List<string>> ReadColumnAsync(SqlConnection conn, string table, string column)
    {
        var values = new List<string>();
        using var cmd = new SqlCommand($"SELECT RTRIM({column}) FROM {table} ORDER BY {column}", conn);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (!reader.IsDBNull(0)) values.Add(reader.GetString(0));
        }
        return values;
    }
}
