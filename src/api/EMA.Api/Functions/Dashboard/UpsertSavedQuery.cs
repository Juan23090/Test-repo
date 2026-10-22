using EMA.Api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace EMA.Api.Functions.Dashboard;

/// <summary>
/// POST /api/savedqueries — creates or overwrites a named saved query for the organization
/// (upsert by name) in dbo.OrganizationDashboard. EMAOrganizationID is supplied in the request body.
/// </summary>
public class UpsertSavedQuery
{
    private readonly ILogger<UpsertSavedQuery> _logger;
    private readonly string _connectionString;

    public UpsertSavedQuery(ILogger<UpsertSavedQuery> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("UpsertSavedQuery")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "savedqueries")] HttpRequest req)
    {
        _logger.LogInformation("UpsertSavedQuery triggered.");

        SavedQueryRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<SavedQueryRequest>(
                req.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult(new { Error = "Request body is not valid JSON." });
        }

        if (body is null)
            return new BadRequestObjectResult(new { Error = "Request body is required." });
        if (string.IsNullOrWhiteSpace(body.Name))
            return new BadRequestObjectResult(new { Error = "Name is required." });

        var organizationID =
            req.GetRequiredApiIdentity().EMAOrganizationID;

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(
                """
                IF EXISTS (
                    SELECT 1 FROM dbo.OrganizationDashboard
                    WHERE EMAOrganizationID = @OrgID AND DashboardName = @Name
                )
                    UPDATE dbo.OrganizationDashboard
                    SET    Filters = @Filters, DateTimeUpdated = @Now, UserIDWhoUpdated = @UserID
                    WHERE  EMAOrganizationID = @OrgID AND DashboardName = @Name;
                ELSE
                    INSERT INTO dbo.OrganizationDashboard
                        (EMAOrganizationID, DashboardName, Filters,
                         DateTimeCreated, UserIDWhoCreated, DateTimeUpdated, UserIDWhoUpdated)
                    VALUES
                        (@OrgID, @Name, @Filters, @Now, @UserID, @Now, @UserID);
                """,
                conn);

            cmd.Parameters.AddWithValue("@OrgID",   organizationID);
            cmd.Parameters.AddWithValue("@Name",    body.Name.Trim());
            cmd.Parameters.AddWithValue("@Filters", body.Filters ?? "{}");
            cmd.Parameters.AddWithValue("@Now",     DateTime.UtcNow);
            cmd.Parameters.AddWithValue("@UserID",  req.GetRequiredApiIdentity().UserID);

            await cmd.ExecuteNonQueryAsync();
            return new OkObjectResult(new { Name = body.Name.Trim() });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving query '{Name}' for EMAOrganizationID {OrgID}", body.Name, organizationID);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }
}
