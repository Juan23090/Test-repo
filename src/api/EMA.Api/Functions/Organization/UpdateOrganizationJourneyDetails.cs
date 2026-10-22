using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace EMA.Api.Functions.Organization;

public sealed class UpdateOrganizationJourneyDetails
{
    private readonly ILogger<UpdateOrganizationJourneyDetails> _logger;
    private readonly string _connectionString;

    public UpdateOrganizationJourneyDetails(ILogger<UpdateOrganizationJourneyDetails> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")
            ?? throw new InvalidOperationException("Missing SqlConnectionString.");
    }

    [Function("UpdateOrganizationJourneyDetails")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "organization/journey-update/{orgID:int}")] HttpRequest request,
        int orgID)
    {
        _logger.LogInformation("UpdateOrganizationJourneyDetails triggered for organization {OrgID}.", orgID);

        try
        {
            var body = await new StreamReader(request.Body).ReadToEndAsync();
            var json = JsonSerializer.Deserialize<JsonElement>(body);

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = """
                UPDATE dbo.Organization
                SET 
                    ContactName = @ContactName,
                    ContactPhone = @ContactPhone,
                    ContactEmail = @ContactEmail,
                    AdminName = @AdminName,
                    AdminPhone = @AdminPhone,
                    AdminEmail = @AdminEmail,
                    AnnualBudget = @AnnualBudget,
                    ChMSID = @ChMSID,
                    AccountingSoftwareID = @AccountingSoftwareID
                WHERE EMAOrganizationID = @OrgID;
                """;

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@OrgID", orgID);
            command.Parameters.AddWithValue("@ContactName", GetValueOrDbNull(json, "contactName"));
            command.Parameters.AddWithValue("@ContactPhone", GetValueOrDbNull(json, "contactPhone"));
            command.Parameters.AddWithValue("@ContactEmail", GetValueOrDbNull(json, "contactEmail"));
            command.Parameters.AddWithValue("@AdminName", GetValueOrDbNull(json, "adminName"));
            command.Parameters.AddWithValue("@AdminPhone", GetValueOrDbNull(json, "adminPhone"));
            command.Parameters.AddWithValue("@AdminEmail", GetValueOrDbNull(json, "adminEmail"));
            command.Parameters.AddWithValue("@AnnualBudget", GetValueOrDbNull(json, "annualBudget"));
            command.Parameters.AddWithValue("@ChMSID", GetValueOrDbNull(json, "chMSID"));
            command.Parameters.AddWithValue("@AccountingSoftwareID", GetValueOrDbNull(json, "accountingSoftwareID"));

            int rowsAffected = await command.ExecuteNonQueryAsync();

            return new OkObjectResult(new { success = rowsAffected > 0 });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error updating organization journey details for organization {OrgID}.", orgID);

            return new ObjectResult(new { message = "Unable to update organization details." })
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

