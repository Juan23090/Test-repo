using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace EMA_Api.Functions.Customization;

/// <summary>
/// GET /api/customization/user-fund-shortlist/{orgID} — the signed-in user's fund shortlist for
/// the organization. Reconciles the list against the org's current funds first (adding new active
/// funds unchecked, dropping inactive ones) so the overlay always reflects reality.
///
/// The user is taken from the request identity, never the route or body — a shortlist is personal.
/// </summary>
public class GetUserFundShortList
{
    private readonly ILogger<GetUserFundShortList> _logger;
    private readonly string _connectionString;

    public GetUserFundShortList(ILogger<GetUserFundShortList> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("GetUserFundShortList")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "customization/user-fund-shortlist/{orgID:int}")] HttpRequest req,
        int orgID)
    {
        _logger.LogInformation("GetUserFundShortList triggered for org {OrgID}.", orgID);

        try
        {
            var identity = req.GetRequiredApiIdentity();

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(
                "EXEC dbo.sp_SyncUserFundShortList @EMAOrganizationID = @EMAOrganizationID, @UserID = @UserID",
                conn);

            cmd.Parameters.AddWithValue("@EMAOrganizationID", identity.EMAOrganizationID);
            cmd.Parameters.AddWithValue("@UserID", identity.UserID);

            var results = new List<object>();

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new
                {
                    EMAFundID = reader.GetInt32(reader.GetOrdinal("EMAFundID")),
                    FundName = reader.IsDBNull(reader.GetOrdinal("FundName"))
                        ? string.Empty
                        : reader.GetString(reader.GetOrdinal("FundName")),
                    Sequence = reader.IsDBNull(reader.GetOrdinal("Sequence"))
                        ? (int?)null
                        : reader.GetInt32(reader.GetOrdinal("Sequence")),
                    // 'Y'/'N' becomes a real bool for the client, as GetFunds does.
                    Selected = !reader.IsDBNull(reader.GetOrdinal("Selected"))
                        && reader.GetString(reader.GetOrdinal("Selected"))
                            .Trim()
                            .Equals("Y", StringComparison.OrdinalIgnoreCase)
                });
            }

            return new OkObjectResult(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching the user fund shortlist for org {OrgID}.", orgID);
            return new StatusCodeResult(500);
        }
    }
}
