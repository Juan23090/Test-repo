using System.Data;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Dashboard;

/// <summary>
/// PUT /api/dashboard/kpis/{orgID}/history-days  body: { "days": NN }
/// Persists the "History Days to Show" value to Organization.KPIDefaultHistoryDays
/// (the "sticky" field on the KPIs page). Valid range 1–3650 (smallint column).
/// </summary>
public sealed class SaveKpiHistoryDays
{
    private readonly ILogger<SaveKpiHistoryDays> _logger;
    private readonly string _connectionString;

    public SaveKpiHistoryDays(ILogger<SaveKpiHistoryDays> logger)
    {
        _logger = logger;
        _connectionString =
            Environment.GetEnvironmentVariable("SqlConnectionString")
            ?? throw new InvalidOperationException("Missing SqlConnectionString.");
    }

    private sealed class HistoryDaysRequest { public int Days { get; set; } }

    [Function("SaveKpiHistoryDays")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "put",
                        Route = "dashboard/kpis/{orgID:int}/history-days")]
        HttpRequest request,
        int orgID)
    {
        _logger.LogInformation("SaveKpiHistoryDays triggered for org {OrgID}.", orgID);

        if (orgID <= 0)
            return new BadRequestObjectResult(new { message = "A valid organization ID is required." });

        int days;
        try
        {
            var body = await JsonSerializer.DeserializeAsync<HistoryDaysRequest>(
                request.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            days = body?.Days ?? 0;
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult(new { message = "Invalid request body." });
        }

        if (days < 1 || days > 3650)
            return new BadRequestObjectResult(new { message = "days must be between 1 and 3650." });

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = """
                UPDATE dbo.Organization
                SET KPIDefaultHistoryDays = @Days
                WHERE EMAOrganizationID = @OrgID;
                """;

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@Days", SqlDbType.SmallInt).Value = days;
            command.Parameters.Add("@OrgID", SqlDbType.Int).Value = orgID;

            var rows = await command.ExecuteNonQueryAsync();
            if (rows == 0)
                return new NotFoundObjectResult(new { message = $"Organization {orgID} not found." });

            return new OkObjectResult(new { Days = days });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error saving KPI history days for org {OrgID}.", orgID);
            return new ObjectResult(new { message = "Unable to save the history-days setting." })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }
}
