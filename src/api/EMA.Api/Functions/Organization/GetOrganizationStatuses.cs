using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Organization;

public sealed class GetOrganizationStatuses
{
    private readonly ILogger<GetOrganizationStatuses> _logger;
    private readonly string _connectionString;

    public GetOrganizationStatuses(ILogger<GetOrganizationStatuses> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")
            ?? throw new InvalidOperationException("Missing SqlConnectionString.");
    }

    [Function("GetOrganizationStatuses")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "organization/statuses")] HttpRequest request)
    {
        _logger.LogInformation("GetOrganizationStatuses triggered.");

        try
        {
            var statuses = new List<Dictionary<string, object?>>();

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = """
                SELECT 
                    OrganizationStatusID,
                    Description
                FROM dbo.OrganizationStatus
                ORDER BY Sequence ASC, OrganizationStatusID ASC;
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
                statuses.Add(entry);
            }

            return new OkObjectResult(statuses);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error loading organization statuses.");

            return new ObjectResult(new { message = "Unable to load statuses." })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }
}

