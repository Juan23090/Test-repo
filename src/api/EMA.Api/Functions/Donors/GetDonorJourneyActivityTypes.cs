using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Donors;

public sealed class GetDonorJourneyActivityTypes
{
    private readonly ILogger<GetDonorJourneyActivityTypes> _logger;
    private readonly string _connectionString;

    public GetDonorJourneyActivityTypes(ILogger<GetDonorJourneyActivityTypes> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")
            ?? throw new InvalidOperationException("Missing SqlConnectionString.");
    }

    [Function("GetDonorJourneyActivityTypes")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "donors/activity-types/{orgID:int}")] HttpRequest request,
        int orgID)
    {
        _logger.LogInformation("GetDonorJourneyActivityTypes triggered for org {OrgID}.", orgID);

        try
        {
            var activityTypes = new List<Dictionary<string, object?>>();

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = """
                SELECT ActivityTypeID, Description 
                FROM dbo.DonorJourneyActivityType 
                WHERE EMAOrganizationID = @OrgID AND IsActive = 'Y' 
                ORDER BY Sequence;
                """;

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@OrgID", orgID);

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var activityType = new Dictionary<string, object?>();
                for (int index = 0; index < reader.FieldCount; index++)
                {
                    string columnName = reader.GetName(index);
                    activityType[columnName] = reader.IsDBNull(index) ? null : reader.GetValue(index);
                }
                activityTypes.Add(activityType);
            }

            return new OkObjectResult(activityTypes);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error loading donor journey activity types for organization {OrgID}.", orgID);

            return new ObjectResult(new { message = "Unable to load activity types." })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }
}

