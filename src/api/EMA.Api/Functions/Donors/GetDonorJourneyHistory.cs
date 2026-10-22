using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Donors;

public sealed class GetDonorJourneyHistory
{
    private readonly ILogger<GetDonorJourneyHistory> _logger;
    private readonly string _connectionString;

    public GetDonorJourneyHistory(ILogger<GetDonorJourneyHistory> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")
            ?? throw new InvalidOperationException("Missing SqlConnectionString.");
    }

    [Function("GetDonorJourneyHistory")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "donors/journey-history/{orgID:int}/{donorID:int}")] HttpRequest request,
        int orgID,
        int donorID)
    {
        _logger.LogInformation("GetDonorJourneyHistory triggered for donor {DonorID} in org {OrgID}.", donorID, orgID);

        try
        {
            var history = new List<Dictionary<string, object?>>();

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = """
                SELECT 
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
                FROM dbo.DonorJourney
                WHERE EMAOrganizationID = @OrgID AND EMADonorId = @DonorID
                ORDER BY ActivityDateTIme DESC;
                """;

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@OrgID", orgID);
            command.Parameters.AddWithValue("@DonorID", donorID);

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var entry = new Dictionary<string, object?>();
                for (int index = 0; index < reader.FieldCount; index++)
                {
                    string columnName = reader.GetName(index);
                    entry[columnName] = reader.IsDBNull(index) ? null : reader.GetValue(index);
                }
                history.Add(entry);
            }

            return new OkObjectResult(history);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error loading donor journey history for donor {DonorID} in organization {OrgID}.", donorID, orgID);

            return new ObjectResult(new { message = "Unable to load journey history." })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }
}

