using EMA.Api.Services.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Dashboard;

/// <summary>
/// GET /api/dashboard/donations-by-month/{orgID} — monthly donation totals for the
/// "Donations by Month" chart. Returns the last 25 months (current month back 24),
/// zero-filling any month with no donation data so the line spans the full window.
/// </summary>
public class GetDonationsByMonth
{
    private readonly ILogger<GetDonationsByMonth> _logger;
    private readonly string _connectionString;

    public GetDonationsByMonth(ILogger<GetDonationsByMonth> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("GetDonationsByMonth")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dashboard/donations-by-month/{orgID:int}")] HttpRequest req,
        int orgID)
    {
        _logger.LogInformation("GetDonationsByMonth triggered for org {OrgID}.", orgID);

        if (!ApiRequestIdentity.TryFromPrincipal(
                req.HttpContext.User,
                out var identity) ||
            identity is null)
        {
            return new UnauthorizedObjectResult(new
            {
                message =
                    "An authenticated session is required."
            });
        }

        // Donor scope always comes from the validated token. A query-string
        // donor ID must never be trusted because another donor in the same
        // organization would otherwise satisfy the tenant boundary.
        var donorScoped =
            identity.ActorType ==
            ApiActorType.Donor;

        int? donorId =
            donorScoped
                ? identity.DonorID
                : null;

        DateTime? from =
            DateTime.TryParse(
                req.Query["from"],
                out var queryFrom)
                ? queryFrom.Date
                : null;

        DateTime? to =
            DateTime.TryParse(
                req.Query["to"],
                out var queryTo)
                ? queryTo.Date
                : null;

        if (donorScoped &&
            (!donorId.HasValue ||
             !from.HasValue ||
             !to.HasValue ||
             from.Value > to.Value))
        {
            return new BadRequestObjectResult(new
            {
                message =
                    "A valid donor dashboard date range is required."
            });
        }

        // First-of-month bounds for the window we emit (inclusive).
        DateTime windowStart, windowEnd;
        if (donorScoped)
        {
            windowStart = new DateTime(from!.Value.Year, from.Value.Month, 1);
            windowEnd   = new DateTime(to!.Value.Year, to.Value.Month, 1);
        }
        else
        {
            windowEnd   = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            windowStart = windowEnd.AddMonths(-24);   // 25 buckets
        }
        int monthCount = ((windowEnd.Year - windowStart.Year) * 12) + (windowEnd.Month - windowStart.Month) + 1;

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            // Org path: the org-wide view. Donor path: aggregate the base table for one donor.
            string sql = donorScoped
                ? @"SELECT DATEFROMPARTS(YEAR(DateTimeReceived), MONTH(DateTimeReceived), 1) AS MonthStartDate,
                           SUM(DonationAmount)   AS TotalDonations,
                           COUNT(*)              AS DonationCount,
                           AVG(DonationAmount)   AS AverageGiftSize
                    FROM   dbo.Donation
                    WHERE  EMAOrganizationID = @OrgID
                      AND  EMADonorID = @DonorID
                      AND  DateTimeReceived IS NOT NULL
                      AND  CAST(DateTimeReceived AS date) >= @From
                      AND  CAST(DateTimeReceived AS date) <= @To
                    GROUP  BY DATEFROMPARTS(YEAR(DateTimeReceived), MONTH(DateTimeReceived), 1)
                    ORDER  BY MonthStartDate;"
                : @"SELECT MonthStartDate, TotalDonations, DonationCount, AverageGiftSize
                    FROM   dbo.vw_Donations_ByMonth
                    WHERE  EMAOrganizationID = @OrgID
                      AND  MonthStartDate >= @WindowStart
                    ORDER  BY MonthStartDate;";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@OrgID", orgID);
            if (donorScoped)
            {
                cmd.Parameters.Add("@DonorID", System.Data.SqlDbType.Int).Value = donorId!.Value;
                cmd.Parameters.Add("@From", System.Data.SqlDbType.Date).Value = windowStart;
                cmd.Parameters.Add("@To", System.Data.SqlDbType.Date).Value = new DateTime(windowEnd.Year, windowEnd.Month, DateTime.DaysInMonth(windowEnd.Year, windowEnd.Month));
            }
            else
            {
                cmd.Parameters.AddWithValue("@WindowStart", windowStart);
            }

            // Read DB results into a dictionary keyed by the month's first day.
            var monthData = new Dictionary<DateTime, object>();
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                DateTime monthStart = reader.GetDateTime(0).Date;
                monthData[new DateTime(monthStart.Year, monthStart.Month, 1)] = new
                {
                    MonthStartDate  = monthStart,
                    TotalDonations  = reader.GetDecimal(1),
                    DonationCount   = reader.GetInt32(2),
                    AverageGiftSize = reader.GetDecimal(3)
                };
            }

            // Emit every month in the window in order (zero-filling empty months) so the line spans it.
            var months = new List<object>();
            for (int i = 0; i < monthCount; i++)
            {
                DateTime monthStart = windowStart.AddMonths(i);
                months.Add(monthData.TryGetValue(monthStart, out var entry)
                    ? entry
                    : new
                    {
                        MonthStartDate  = monthStart,
                        TotalDonations  = 0m,
                        DonationCount   = 0,
                        AverageGiftSize = 0m
                    });
            }

            return new OkObjectResult(new { Months = months });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching donations by month for EMAOrganizationID {OrgID}", orgID);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }
}

