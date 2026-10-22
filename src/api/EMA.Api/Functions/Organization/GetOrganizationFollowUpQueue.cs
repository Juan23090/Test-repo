using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Organization;

public sealed class GetOrganizationFollowUpQueue
{
    private readonly ILogger<GetOrganizationFollowUpQueue> _logger;
    private readonly string _connectionString;

    public GetOrganizationFollowUpQueue(ILogger<GetOrganizationFollowUpQueue> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")
            ?? throw new InvalidOperationException("Missing SqlConnectionString.");
    }

    [Function("GetOrganizationFollowUpQueue")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "organization/followups")] HttpRequest request)
    {
        _logger.LogInformation("GetOrganizationFollowUpQueue triggered.");

        try
        {
            var followups = new List<Dictionary<string, object?>>();

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = """
                SELECT 
                    j.OrganizationInteractionID,
                    j.EMAOrganizationID,
                    o.OrganizationName,
                    at.ActivityDescription,
                    j.FollowUpDateTime,
                    os.Description AS StatusDescription
                FROM dbo.OrganizationJourney j
                INNER JOIN dbo.Organization o ON j.EMAOrganizationID = o.EMAOrganizationID
                LEFT JOIN dbo.OrganizationJourneyActivityType at ON j.FollowUpActivityTypeID = at.OrganizationJournalActivityTypeID
                LEFT JOIN dbo.OrganizationStatus os ON j.OrganizationStatusID = os.OrganizationStatusID
                WHERE j.FollowUpDateTime IS NOT NULL
                ORDER BY j.FollowUpDateTime ASC;
                """;

            await using var command = new SqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var entry = new Dictionary<string, object?>();
                for (int index = 0; index < reader.FieldCount; index++)
                {
                    string columnName = reader.GetName(index);
                    entry[columnName] = reader.IsDBNull(index) ? null : reader.GetValue(index);
                }
                followups.Add(entry);
            }

            return new OkObjectResult(followups);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error loading organization follow-up queue.");

            return new ObjectResult(new { message = "Unable to load follow-up queue." })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }
}

