using System.Text.Json;
using EMA.Api.DTOs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace EMA_Api.Functions.Organization;

/// <summary>
/// PUT /api/chms/{id} — updates an existing ChMS reference row (ChMS admin screen).
/// Updates every editable column plus the DateTimeUpdated / UpdatedbyUserID audit columns.
/// </summary>
public class PutChMS
{
    private readonly ILogger<PutChMS> _logger;
    private readonly string _connectionString;

    public PutChMS(ILogger<PutChMS> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("PutChMS")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "chms/{id:int}")] HttpRequest req,
        int id)
    {
        _logger.LogInformation("PutChMS triggered for ChMSID {ChMSID}.", id);

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

            const string sql = @"
                UPDATE dbo.ChMS SET
                    ChMSName                       = @ChMSName,
                    Country                        = @Country,
                    CanadianInstalls               = @CanadianInstalls,
                    ChMSCandidateForEMAID          = @ChMSCandidateForEMAID,
                    ChMSCandidateRatingID          = @ChMSCandidateRatingID,
                    ChMSAPIRating                  = @ChMSAPIRating,
                    ChMSCandidateDecisionBackgound = @ChMSCandidateDecisionBackgound,
                    MirrorTransactionEmails        = @MirrorTransactionEmails,
                    ChMSMonthlyRentDefault         = @ChMSMonthlyRentDefault,
                    ChMSPercentBasedFee            = @ChMSPercentBasedFee,
                    ChMSPerTransactionFee          = @ChMSPerTransactionFee,
                    ChMSURL                        = @ChMSURL,
                    ChMSAPIDocURL                  = @ChMSAPIDocURL,
                    ChMSOnboardingDocumentURL      = @ChMSOnboardingDocumentURL,
                    ChMSOnboardingVideoURL         = @ChMSOnboardingVideoURL,
                    ChMSDataPortURL                = @ChMSDataPortURL,
                    DateTimeUpdated                = GETDATE(),
                    UpdatedbyUserID                = @UserID
                WHERE ChMSID = @ChMSID;";

            using var cmd = new SqlCommand(sql, conn);
            ChMSHelpers.AddEditableParameters(cmd, dto);
            cmd.Parameters.AddWithValue("@UserID", req.GetRequiredApiIdentity().UserID);
            cmd.Parameters.AddWithValue("@ChMSID", id);

            var rows = await cmd.ExecuteNonQueryAsync();
            if (rows == 0)
                return new NotFoundObjectResult(new { message = $"ChMS {id} not found." });

            return new OkObjectResult(new { ChMSID = id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating ChMS {ChMSID}.", id);
            return new StatusCodeResult(500);
        }
    }
}
