using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Organization;

public sealed class GetOrganizationJourneyActivityTypes
{
    private readonly ILogger<GetOrganizationJourneyActivityTypes> _logger;
    private readonly string _connectionString;

    public GetOrganizationJourneyActivityTypes(ILogger<GetOrganizationJourneyActivityTypes> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")
            ?? throw new InvalidOperationException("Missing SqlConnectionString.");
    }

    [Function("GetOrganizationJourneyActivityTypes")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "organization/journey-activity-types")] HttpRequest request)
    {
        _logger.LogInformation("GetOrganizationJourneyActivityTypes triggered.");

        try
        {
            var activityTypes = new List<Dictionary<string, object?>>();

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = """
                SELECT 
                    OrganizationJournalActivityTypeID,
                    Sequence,
                    ActivityDescription,
                    IsActive
                FROM dbo.OrganizationJourneyActivityType
                WHERE IsActive = 'Y'
                ORDER BY Sequence ASC, OrganizationJournalActivityTypeID ASC;
                """;

            await using var command = new SqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var type = new Dictionary<string, object?>();
                for (int index = 0; index < reader.FieldCount; index++)
                {
                    string columnName = reader.GetName(index);
                    type[columnName] = reader.IsDBNull(index) ? null : reader.GetValue(index);
                }
                activityTypes.Add(type);
            }

            return new OkObjectResult(activityTypes);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error loading organization journey activity types.");

            return new ObjectResult(new { message = "Unable to load activity types." })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }
}

