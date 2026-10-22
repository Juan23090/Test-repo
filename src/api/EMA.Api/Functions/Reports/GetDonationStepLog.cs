using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace EMA_Api.Functions.Reports;

public class GetDonationStepLog
{
    private readonly ILogger<GetDonationStepLog> _logger;
    private readonly string _connectionString;

    public GetDonationStepLog(ILogger<GetDonationStepLog> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString");
    }

    [Function("GetDonationStepLog")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "email/steps/{donationID:long}")] HttpRequest req,
        long donationID)
    {
        _logger.LogInformation("GetDonationStepLog triggered for donation {DonationID}.", donationID);
        var results = new List<Dictionary<string, object?>>();
        try
        {
            var organizationID =
                req.GetRequiredApiIdentity()
                    .EMAOrganizationID;

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                @"SELECT TOP (100)
                      l.DonationStepID,
                      s.Description,
                      l.DateTimeLogged
                  FROM dbo.DonationStepLog AS l
                  INNER JOIN dbo.DonationStep AS s
                      ON s.DonationStepID = l.DonationStepID
                  WHERE l.EMADonationID = @DonationID
                    AND EXISTS
                    (
                        SELECT 1
                        FROM dbo.Donation AS d
                        WHERE d.EMADonationID = l.EMADonationID
                          AND d.EMAOrganizationID = @OrgID
                    )
                  ORDER BY l.DateTimeLogged DESC, l.DonationStepID DESC;",
                conn);
            cmd.CommandTimeout = 10;
            cmd.Parameters.Add("@DonationID", System.Data.SqlDbType.BigInt).Value = donationID;
            cmd.Parameters.Add("@OrgID", System.Data.SqlDbType.Int).Value = organizationID;
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new Dictionary<string, object?>
                {
                    ["DonationStepID"]  = reader.IsDBNull(0) ? null : reader.GetInt32(0),
                    ["Description"]     = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    ["DateTimeLogged"]  = reader.IsDBNull(2) ? string.Empty : reader.GetDateTime(2).ToString("yyyy-MM-dd HH:mm:ss")
                });
            }
            return new OkObjectResult(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching donation step log.");
            return new StatusCodeResult(500);
        }
    }
}
