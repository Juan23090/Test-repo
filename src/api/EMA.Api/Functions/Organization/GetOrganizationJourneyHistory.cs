using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Organization;

public sealed class GetOrganizationJourneyHistory
{
    private readonly ILogger<GetOrganizationJourneyHistory> _logger;
    private readonly string _connectionString;

    public GetOrganizationJourneyHistory(ILogger<GetOrganizationJourneyHistory> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")
            ?? throw new InvalidOperationException("Missing SqlConnectionString.");
    }

    [Function("GetOrganizationJourneyHistory")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "organization/journey-history/{orgID:int}")] HttpRequest request,
        int orgID)
    {
        _logger.LogInformation("GetOrganizationJourneyHistory triggered for organization {OrgID}.", orgID);

        try
        {
            var history = new List<Dictionary<string, object?>>();

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = """
                SELECT 
                    j.OrganizationInteractionID,
                    j.EMAOrganizationID,
                    j.EMAUserID AS UserID,
                    u.UserName,
                    j.MenuItemID,
                    j.ActivityDateTime,
                    j.ActivityTypeID,
                    j.EnhancementRequestID,
                    j.FAQID,
                    j.SupportTicketID,
                    at.ActivityDescription,
                    j.InteractionComments, 
                    j.OrganizationStatusID,
                    os.Description AS OrganizationStatusDescription,
                    j.FollowUpDateTime, 
                    j.FollowUpActivityTypeID
                FROM dbo.OrganizationJourney j
                LEFT JOIN dbo.[User] u ON j.EMAUserID = u.UserID
                LEFT JOIN dbo.OrganizationJourneyActivityType at ON j.ActivityTypeID = at.OrganizationJournalActivityTypeID
                LEFT JOIN dbo.OrganizationStatus os ON j.OrganizationStatusID = os.OrganizationStatusID
                WHERE j.EMAOrganizationID = @OrgID
                ORDER BY j.ActivityDateTime DESC;
                """;

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@OrgID", orgID);

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
            _logger.LogError(exception, "Error loading organization journey history for organization {OrgID}.", orgID);

            return new ObjectResult(new { message = "Unable to load journey history." })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }
}

