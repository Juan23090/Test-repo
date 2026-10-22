using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace EMA.Api.Functions.Organization;

public sealed class CreateOrganizationJourneyEntry
{
    private readonly ILogger<CreateOrganizationJourneyEntry> _logger;
    private readonly string _connectionString;

    public CreateOrganizationJourneyEntry(ILogger<CreateOrganizationJourneyEntry> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")
            ?? throw new InvalidOperationException("Missing SqlConnectionString.");
    }

    [Function("CreateOrganizationJourneyEntry")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "organization/journey-history/{orgID:int}")] HttpRequest request,
        int orgID)
    {
        _logger.LogInformation("CreateOrganizationJourneyEntry triggered for organization {OrgID}.", orgID);

        try
        {
            var body = await new StreamReader(request.Body).ReadToEndAsync();
            var json = JsonSerializer.Deserialize<JsonElement>(body);

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = """
                INSERT INTO dbo.OrganizationJourney (
                    EMAOrganizationID, 
                    EMAUserID,
                    MenuItemID,
                    ActivityDateTime, 
                    ActivityTypeID, 
                    InteractionComments, 
                    OrganizationStatusID,
                    FollowUpDateTime, 
                    FollowUpActivityTypeID
                ) VALUES (
                    @OrgID, 
                    @UserID,
                    @MenuItemID,
                    GETUTCDATE(),
                    @ActivityTypeID, 
                    @InteractionComments, 
                    ISNULL(@OrganizationStatusID, (SELECT ISNULL(OrganizationStatusID, 1) FROM dbo.Organization WHERE EMAOrganizationID = @OrgID)),
                    @FollowUpDateTime, 
                    @FollowUpActivityTypeID
                );
                SELECT SCOPE_IDENTITY();
                """;

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@OrgID", orgID);
            command.Parameters.AddWithValue(
                "@UserID",
                request.GetRequiredApiIdentity().UserID);
            command.Parameters.AddWithValue("@MenuItemID", GetValueOrDbNull(json, "menuItemID"));
            command.Parameters.AddWithValue("@ActivityTypeID", GetValueOrDbNull(json, "activityTypeID"));
            command.Parameters.AddWithValue("@InteractionComments", GetValueOrDbNull(json, "interactionComments"));
            command.Parameters.AddWithValue("@OrganizationStatusID", GetValueOrDbNull(json, "organizationStatusID"));
            command.Parameters.AddWithValue("@FollowUpDateTime", GetValueOrDbNull(json, "followUpDateTime"));
            command.Parameters.AddWithValue("@FollowUpActivityTypeID", GetValueOrDbNull(json, "followUpActivityTypeID"));

            var result = await command.ExecuteScalarAsync();
            int insertedId = Convert.ToInt32(result);

            return new OkObjectResult(new { organizationInteractionID = insertedId });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error creating organization journey entry for organization {OrgID}.", orgID);

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
