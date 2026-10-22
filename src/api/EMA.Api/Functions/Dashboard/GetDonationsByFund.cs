using EMA.Api.Services.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Dashboard;

/// <summary>
/// GET /api/dashboard/donations-by-fund/{orgID} — total donations grouped by fund name
/// for the Donations by Fund doughnut chart. Sums DonationDistribution.DistributionAmount
/// (not Donation.DonationAmount) so split donations are counted per-fund, not duplicated.
/// </summary>
public class GetDonationsByFund
{
    private readonly ILogger<GetDonationsByFund> _logger;
    private readonly string _connectionString;

    public GetDonationsByFund(ILogger<GetDonationsByFund> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("GetDonationsByFund")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dashboard/donations-by-fund/{orgID:int}")] HttpRequest req,
        int orgID)
    {
        _logger.LogInformation("GetDonationsByFund triggered for org {OrgID}.", orgID);

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

        var donorScoped =
            identity.ActorType ==
            ApiActorType.Donor;

        int? days =
            !donorScoped &&
            int.TryParse(
                req.Query["days"],
                out var parsedDays) &&
            parsedDays is >= 1 and <= 3650
                ? parsedDays
                : null;

        int? donorId =
            donorScoped
                ? identity.DonorID
                : null;

        DateTime? from =
            donorScoped &&
            DateTime.TryParse(
                req.Query["from"],
                out var queryFrom)
                ? queryFrom.Date
                : null;

        DateTime? to =
            donorScoped &&
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

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            const string Sql = @"
                SELECT f.Name, SUM(dd.DistributionAmount) AS TotalDonations
                FROM   dbo.Donation d
                JOIN   dbo.DonationDistribution dd ON d.EMADonationID = dd.EMADonationID
                JOIN   dbo.OrganizationFund f       ON dd.EMAFundID   = f.EMAFundID
                WHERE  d.EMAOrganizationID = @OrgID
                  AND  d.DateTimeReceived IS NOT NULL
                  AND  (@DonorID IS NULL OR d.EMADonorID = @DonorID)
                  AND  (@Days IS NULL OR d.DateTimeReceived >= DATEADD(day, -@Days, CAST(GETDATE() AS date)))
                  AND  (@From IS NULL OR CAST(d.DateTimeReceived AS date) >= @From)
                  AND  (@To   IS NULL OR CAST(d.DateTimeReceived AS date) <= @To)
                GROUP  BY f.Name
                ORDER  BY TotalDonations DESC;";

            using var cmd = new SqlCommand(Sql, conn);
            cmd.Parameters.AddWithValue("@OrgID", orgID);
            // Type @Days as Int explicitly so "-@Days" is valid even when NULL (all-time);
            // AddWithValue(DBNull) would infer nvarchar and break the unary minus.
            cmd.Parameters.Add("@Days", System.Data.SqlDbType.Int).Value = (object?)days ?? DBNull.Value;
            cmd.Parameters.Add("@DonorID", System.Data.SqlDbType.Int).Value = (object?)donorId ?? DBNull.Value;
            cmd.Parameters.Add("@From", System.Data.SqlDbType.Date).Value = (object?)from ?? DBNull.Value;
            cmd.Parameters.Add("@To", System.Data.SqlDbType.Date).Value = (object?)to ?? DBNull.Value;

            var items = new List<object>();
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                items.Add(new
                {
                    Label  = reader.GetString(0),
                    Amount = reader.GetDecimal(1)
                });
            }

            return new OkObjectResult(items);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching donations by fund for EMAOrganizationID {OrgID}", orgID);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }
}

