using EMA.Api.DTOs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace EMA_Api.Functions.Organization;

/// <summary>
/// GET /api/chms — full ChMS reference list for the ChMS admin screen (/chms grid + edit form).
/// Returns every editable column plus read-only audit timestamps.
/// </summary>
public class GetChMS
{
    private readonly ILogger<GetChMS> _logger;
    private readonly string _connectionString;

    public GetChMS(ILogger<GetChMS> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("GetChMS")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "chms")] HttpRequest req)
    {
        _logger.LogInformation("GetChMS triggered.");

        var results = new List<ChMSDTO>();

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            // char(30) FK columns are space-padded — RTRIM so values match the lookup options.
            using var cmd = new SqlCommand(
                @"SELECT ChMSID,
                         ChMSName,
                         Country,
                         CanadianInstalls,
                         RTRIM(ChMSCandidateForEMAID) AS ChMSCandidateForEMAID,
                         RTRIM(ChMSCandidateRatingID) AS ChMSCandidateRatingID,
                         RTRIM(ChMSAPIRating)         AS ChMSAPIRating,
                         ChMSCandidateDecisionBackgound,
                         MirrorTransactionEmails,
                         ChMSMonthlyRentDefault,
                         ChMSPercentBasedFee,
                         ChMSPerTransactionFee,
                         ChMSURL,
                         ChMSAPIDocURL,
                         ChMSOnboardingDocumentURL,
                         ChMSOnboardingVideoURL,
                         ChMSDataPortURL,
                         DateTimeCreated,
                         DateTimeUpdated
                  FROM dbo.ChMS
                  ORDER BY ChMSName",
                conn);

            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                results.Add(new ChMSDTO
                {
                    ChMSID                         = reader.GetInt32(0),
                    ChMSName                       = reader.GetString(1),
                    Country                        = reader.IsDBNull(2) ? null : reader.GetString(2),
                    CanadianInstalls               = reader.IsDBNull(3) ? null : reader.GetInt16(3),
                    ChMSCandidateForEMAID          = reader.IsDBNull(4) ? null : reader.GetString(4),
                    ChMSCandidateRatingID          = reader.IsDBNull(5) ? null : reader.GetString(5),
                    ChMSAPIRating                  = reader.IsDBNull(6) ? null : reader.GetString(6),
                    ChMSCandidateDecisionBackgound = reader.IsDBNull(7) ? null : reader.GetString(7),
                    MirrorTransactionEmails        = reader.IsDBNull(8) ? null : reader.GetString(8),
                    ChMSMonthlyRentDefault         = reader.IsDBNull(9) ? null : reader.GetDecimal(9),
                    ChMSPercentBasedFee            = reader.IsDBNull(10) ? null : reader.GetDecimal(10),
                    ChMSPerTransactionFee          = reader.IsDBNull(11) ? null : reader.GetDecimal(11),
                    ChMSURL                        = reader.IsDBNull(12) ? null : reader.GetString(12),
                    ChMSAPIDocURL                  = reader.IsDBNull(13) ? null : reader.GetString(13),
                    ChMSOnboardingDocumentURL      = reader.IsDBNull(14) ? null : reader.GetString(14),
                    ChMSOnboardingVideoURL         = reader.IsDBNull(15) ? null : reader.GetString(15),
                    ChMSDataPortURL                = reader.IsDBNull(16) ? null : reader.GetString(16),
                    DateTimeCreated                = reader.IsDBNull(17) ? null : reader.GetDateTime(17),
                    DateTimeUpdated                = reader.IsDBNull(18) ? null : reader.GetDateTime(18)
                });
            }

            return new OkObjectResult(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching ChMS.");
            return new StatusCodeResult(500);
        }
    }
}
