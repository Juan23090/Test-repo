using System.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Dashboard;

/// <summary>
/// GET /api/dashboard/kpis/{orgID}?days=NN
/// Returns the KPI tile values for the selected organization over the last NN days:
/// NumberDonations, AverageDonation, DonorsAdded, DonorsActive.
/// If ?days is omitted it falls back to the org's KPIDefaultHistoryDays (default 7).
/// The effective Days is returned so the UI can populate the "History Days" input.
/// </summary>
public sealed class GetDashboardKpis
{
    private readonly ILogger<GetDashboardKpis> _logger;
    private readonly string _connectionString;

    public GetDashboardKpis(
        ILogger<GetDashboardKpis> logger)
    {
        _logger = logger;

        _connectionString =
            Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString.");
    }

    [Function("GetDashboardKpis")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
                        Route = "dashboard/kpis/{orgID:int}")]
        HttpRequest request,
        int orgID)
    {
        _logger.LogInformation(
            "GetDashboardKpis triggered for org {OrgID}.",
            orgID);

        if (orgID <= 0)
        {
            return new BadRequestObjectResult(new
            {
                message =
                    "A valid organization ID is required."
            });
        }

        try
        {
            await using var connection =
                new SqlConnection(
                    _connectionString);

            await connection.OpenAsync();

            // Window size: explicit ?days wins; otherwise the org's saved default.
            var days =
                TryReadDays(request, out var requested)
                    ? requested
                    : await GetOrgDefaultHistoryDaysAsync(connection, orgID);

            var numberDonations =
                await GetNumberOfDonationsAsync(connection, orgID, days);

            var averageDonation =
                await GetAverageDonationAsync(connection, orgID, days);

            var donorsAdded =
                await GetDonorsAddedAsync(connection, orgID, days);

            var donorsActive =
                await GetDonorsActiveAsync(connection, orgID, days);

            return new OkObjectResult(new
            {
                Days = days,
                NumberDonations = numberDonations,
                AverageDonation = averageDonation,
                DonorsAdded = donorsAdded,
                DonorsActive = donorsActive
            });
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error fetching KPIs for EMAOrganizationID {OrgID}.",
                orgID);

            return new ObjectResult(new
            {
                message =
                    "Unable to load dashboard KPI data."
            })
            {
                StatusCode =
                    StatusCodes
                        .Status500InternalServerError
            };
        }
    }

    // ── Window resolution ─────────────────────────────────────────────────────

    private static bool TryReadDays(HttpRequest request, out int days)
    {
        days = 7;
        var raw = request.Query["days"].ToString();
        if (int.TryParse(raw, out var parsed) && parsed >= 1 && parsed <= 3650)
        {
            days = parsed;
            return true;
        }
        return false;
    }

    private static async Task<int> GetOrgDefaultHistoryDaysAsync(
        SqlConnection connection, int orgID)
    {
        const string sql = """
            SELECT ISNULL(KPIDefaultHistoryDays, 7)
            FROM dbo.Organization
            WHERE EMAOrganizationID = @OrgID;
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@OrgID", SqlDbType.Int).Value = orgID;

        var result = await command.ExecuteScalarAsync();
        return result is null || result is DBNull ? 7 : Convert.ToInt32(result);
    }

    // ── Metrics (all over the last @Days days of DateTimeReceived) ─────────────

    private static async Task<int> GetNumberOfDonationsAsync(
        SqlConnection connection, int orgID, int days)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM dbo.Donation
            WHERE EMAOrganizationID = @OrgID
              AND DateTimeReceived IS NOT NULL
              AND DateTimeReceived >= DATEADD(day, -@Days, CAST(GETDATE() AS date));
            """;
        return await ScalarIntAsync(connection, sql, orgID, days);
    }

    private static async Task<decimal> GetAverageDonationAsync(
        SqlConnection connection, int orgID, int days)
    {
        const string sql = """
            SELECT ISNULL(AVG(DonationAmount), 0)
            FROM dbo.Donation
            WHERE EMAOrganizationID = @OrgID
              AND DateTimeReceived IS NOT NULL
              AND DateTimeReceived >= DATEADD(day, -@Days, CAST(GETDATE() AS date));
            """;
        return await ScalarDecimalAsync(connection, sql, orgID, days);
    }

    private static async Task<int> GetDonorsActiveAsync(
        SqlConnection connection, int orgID, int days)
    {
        // Distinct donors who gave at least once in the window.
        const string sql = """
            SELECT COUNT(DISTINCT EMADonorID)
            FROM dbo.Donation
            WHERE EMAOrganizationID = @OrgID
              AND DateTimeReceived IS NOT NULL
              AND DateTimeReceived >= DATEADD(day, -@Days, CAST(GETDATE() AS date));
            """;
        return await ScalarIntAsync(connection, sql, orgID, days);
    }

    private static async Task<int> GetDonorsAddedAsync(
        SqlConnection connection, int orgID, int days)
    {
        // New donor records created in the window.
        const string sql = """
            SELECT COUNT(*)
            FROM dbo.Donor
            WHERE EMAOrganizationID = @OrgID
              AND DateTimeCreated IS NOT NULL
              AND DateTimeCreated >= DATEADD(day, -@Days, CAST(GETDATE() AS date));
            """;
        return await ScalarIntAsync(connection, sql, orgID, days);
    }

    // ── Small helpers ─────────────────────────────────────────────────────────

    private static async Task<int> ScalarIntAsync(
        SqlConnection connection, string sql, int orgID, int days)
    {
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@OrgID", SqlDbType.Int).Value = orgID;
        command.Parameters.Add("@Days", SqlDbType.Int).Value = days;
        var result = await command.ExecuteScalarAsync();
        return result is null || result is DBNull ? 0 : Convert.ToInt32(result);
    }

    private static async Task<decimal> ScalarDecimalAsync(
        SqlConnection connection, string sql, int orgID, int days)
    {
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@OrgID", SqlDbType.Int).Value = orgID;
        command.Parameters.Add("@Days", SqlDbType.Int).Value = days;
        var result = await command.ExecuteScalarAsync();
        return result is null || result is DBNull ? 0 : Convert.ToDecimal(result);
    }

}
