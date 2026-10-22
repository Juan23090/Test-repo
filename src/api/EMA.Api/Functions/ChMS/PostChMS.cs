using System.Text.Json;
using EMA.Api.DTOs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace EMA_Api.Functions.Organization;

/// <summary>
/// POST /api/chms — inserts a new ChMS reference row (ChMS admin screen).
/// ChMSID is an IDENTITY column, so SQL assigns it and we return SCOPE_IDENTITY().
/// Audit columns are server-set. Returns the new ChMSID.
/// </summary>
public class PostChMS
{
    private readonly ILogger<PostChMS> _logger;
    private readonly string _connectionString;

    public PostChMS(ILogger<PostChMS> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("PostChMS")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "chms")] HttpRequest req)
    {
        _logger.LogInformation("PostChMS triggered.");

        // OPTIONS preflight for /api/chms is handled by GetChMS (same route) to avoid a
        // duplicate-route registration conflict in the isolated worker.
        ChMSDTO? dto;
        try
        {
            dto = await JsonSerializer.DeserializeAsync<ChMSDTO>(
                req.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult(new { message = "Invalid request body." });
        }

        if (dto is null || string.IsNullOrWhiteSpace(dto.ChMSName))
            return new BadRequestObjectResult(new { message = "ChMSName is required." });

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            // ChMSID is an IDENTITY column — let SQL assign it and return SCOPE_IDENTITY().
            const string sql = @"
                INSERT INTO dbo.ChMS
                    (ChMSName, Country, CanadianInstalls, ChMSCandidateForEMAID, ChMSCandidateRatingID,
                     ChMSAPIRating, ChMSCandidateDecisionBackgound, MirrorTransactionEmails, ChMSMonthlyRentDefault,
                     ChMSPercentBasedFee, ChMSPerTransactionFee, ChMSURL, ChMSAPIDocURL,
                     ChMSOnboardingDocumentURL, ChMSOnboardingVideoURL, ChMSDataPortURL,
                     DateTimeCreated, CreatedbyUserID, DateTimeUpdated, UpdatedbyUserID)
                VALUES
                    (@ChMSName, @Country, @CanadianInstalls, @ChMSCandidateForEMAID, @ChMSCandidateRatingID,
                     @ChMSAPIRating, @ChMSCandidateDecisionBackgound, @MirrorTransactionEmails, @ChMSMonthlyRentDefault,
                     @ChMSPercentBasedFee, @ChMSPerTransactionFee, @ChMSURL, @ChMSAPIDocURL,
                     @ChMSOnboardingDocumentURL, @ChMSOnboardingVideoURL, @ChMSDataPortURL,
                     GETDATE(), @UserID, GETDATE(), @UserID);
                SELECT CAST(SCOPE_IDENTITY() AS int);";

            using var cmd = new SqlCommand(sql, conn);
            ChMSHelpers.AddEditableParameters(cmd, dto);
            cmd.Parameters.AddWithValue("@UserID", req.GetRequiredApiIdentity().UserID);

            var newID = await cmd.ExecuteScalarAsync();
            return new OkObjectResult(new { ChMSID = newID });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inserting ChMS.");
            return new StatusCodeResult(500);
        }
    }
}

