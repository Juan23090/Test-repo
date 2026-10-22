using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Donors;

public sealed class GetOrganizationUsers
{
    private readonly ILogger<GetOrganizationUsers> _logger;
    private readonly string _connectionString;

    public GetOrganizationUsers(ILogger<GetOrganizationUsers> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")
            ?? throw new InvalidOperationException("Missing SqlConnectionString.");
    }

    [Function("GetOrganizationUsers")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "donors/users/{orgID:int}")] HttpRequest request,
        int orgID)
    {
        _logger.LogInformation("GetOrganizationUsers triggered for org {OrgID}.", orgID);

        try
        {
            var users = new List<Dictionary<string, object?>>();

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = """
                SELECT u.UserID, u.UserName, u.EmailAddress
                FROM dbo.[User] u
                JOIN dbo.OrganizationUser ou ON u.UserID = ou.UserID
                WHERE ou.EMAOrganizationID = @OrgID 
                  AND u.IsActive = 1 
                  AND ou.OrganizationUserStatusID = 10 -- User Added to Organization (Active)
                ORDER BY u.UserName;
                """;

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@OrgID", orgID);

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var user = new Dictionary<string, object?>();
                for (int index = 0; index < reader.FieldCount; index++)
                {
                    string columnName = reader.GetName(index);
                    user[columnName] = reader.IsDBNull(index) ? null : reader.GetValue(index);
                }
                users.Add(user);
            }

            return new OkObjectResult(users);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error loading organization users for organization {OrgID}.", orgID);

            return new ObjectResult(new { message = "Unable to load organization users." })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }
}

