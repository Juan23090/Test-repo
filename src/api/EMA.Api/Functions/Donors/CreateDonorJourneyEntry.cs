using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace EMA.Api.Functions.Donors;

public sealed class CreateDonorJourneyEntry
{
    private readonly ILogger<CreateDonorJourneyEntry> _logger;
    private readonly string _connectionString;

    public CreateDonorJourneyEntry(ILogger<CreateDonorJourneyEntry> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")
            ?? throw new InvalidOperationException("Missing SqlConnectionString.");
    }

    [Function("CreateDonorJourneyEntry")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "donors/journey-history/{orgID:int}/{donorID:int}")] HttpRequest request,
        int orgID,
        int donorID)
    {
        _logger.LogInformation("CreateDonorJourneyEntry triggered for donor {DonorID} in org {OrgID}.", donorID, orgID);

        try
        {
            var body = await new StreamReader(request.Body).ReadToEndAsync();
            var json = JsonSerializer.Deserialize<JsonElement>(body);

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            string donorInteractionID = "";
            int attempts = 0;
            const int maxAttempts = 5;
            bool success = false;

            while (!success && attempts < maxAttempts)
            {
                donorInteractionID = Guid.NewGuid().ToString("N").Substring(0, 10);
                attempts++;

                try
                {
                    const string sql = """
                        INSERT INTO dbo.DonorJourney (
                            EMAOrganizationID, 
                            EMADonorId, 
                            UserId, 
                            DonorInteractionId,
                            -- Schema detail: 'ActivityDateTIme' and 'FollowUpDateTIme' use capital 'TI' in this table.
                            ActivityDateTIme, 
                            ActivityTypeID, 
                            ActivityComments, 
                            DonorStatusID,
                            FollowUpDateTIme, 
                            FollowUpActivityTypeID
                        ) VALUES (
                            @OrgID, 
                            @DonorID, 
                            @UserID, 
                            @DonorInteractionID,
                            GETDATE(), 
                            @ActivityTypeID, 
                            @ActivityComments, 
                            @DonorStatusID,
                            @FollowUpDateTime, 
                            @FollowUpActivityTypeID
                        );
                        """;

                    await using var command = new SqlCommand(sql, connection);
                    command.Parameters.Clear();
                    command.Parameters.AddWithValue("@OrgID", orgID);
                    command.Parameters.AddWithValue("@DonorID", donorID);
                    command.Parameters.AddWithValue("@DonorInteractionID", donorInteractionID);

                    command.Parameters.AddWithValue(
                        "@UserID",
                        request.GetRequiredApiIdentity().UserID);
                    command.Parameters.AddWithValue("@ActivityTypeID", GetValueOrDbNull(json, "activityTypeID"));
                    command.Parameters.AddWithValue("@ActivityComments", GetValueOrDbNull(json, "activityComments"));
                    command.Parameters.AddWithValue("@DonorStatusID", GetValueOrDbNull(json, "donorStatusID"));
                    command.Parameters.AddWithValue("@FollowUpDateTime", GetValueOrDbNull(json, "followUpDateTime"));
                    command.Parameters.AddWithValue("@FollowUpActivityTypeID", GetValueOrDbNull(json, "followUpActivityTypeID"));

                    await command.ExecuteNonQueryAsync();
                    success = true;
                }
                catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
                {
                    if (attempts >= maxAttempts)
                    {
                        throw;
                    }
                    _logger.LogWarning("DonorInteractionId collision detected for '{ID}'. Retrying with a new ID (Attempt {Attempt}).", donorInteractionID, attempts);
                }
            }

            return new OkObjectResult(new { donorInteractionID });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error creating donor journey entry for donor {DonorID} in organization {OrgID}.", donorID, orgID);

            return new ObjectResult(new { message = "Unable to save journey entry." })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }

    private static object GetValueOrDbNull(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind != JsonValueKind.Null)
        {
            return prop.ValueKind switch
            {
                JsonValueKind.String => prop.GetString() ?? (object)DBNull.Value,
                JsonValueKind.Number => prop.TryGetInt32(out int i) ? (object)i : (object)prop.GetDecimal(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => DBNull.Value
            };
        }
        return DBNull.Value;
    }
}
