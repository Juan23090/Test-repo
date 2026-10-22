using System.Data;
using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Donations;

public class GetDonationAudit
{
    private readonly ILogger<GetDonationAudit> _logger;
    private readonly string _connectionString;

    public GetDonationAudit(
        ILogger<GetDonationAudit> logger)
    {
        _logger =
            logger;

        _connectionString =
            Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing 'SqlConnectionString' env var.");
    }

    [Function("GetDonationAudit")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "donations/{orgID:int}/{donationID:long}/audit")]
        HttpRequestData req,
        int orgID,
        long donationID)
    {
        try
        {
            var organizationID =
                req.GetRequiredApiIdentity()
                    .EMAOrganizationID;

            if (organizationID != orgID)
            {
                var forbidden =
                    req.CreateResponse(
                        HttpStatusCode.Forbidden);

                await forbidden.WriteAsJsonAsync(new
                {
                    message =
                        "The requested organization does not match the authenticated session."
                });

                return forbidden;
            }

            var audits = new List<object>();

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = new SqlCommand(
                @"SELECT
                      audit.DateTimeCreated,
                      audit.UpdatedByUserID,
                      audit.DDDDJsonPayload
                  FROM DonationAudit AS audit
                  INNER JOIN Donation AS donation
                      ON donation.EMADonationID = audit.EMADonationID
                     AND donation.EMAOrganizationID = @OrgID
                  WHERE audit.EMADonationID = @ID
                  ORDER BY
                      audit.DateTimeCreated DESC",
                conn);
            cmd.Parameters.AddWithValue("@ID", donationID);
            cmd.Parameters.AddWithValue(
                "@OrgID",
                organizationID);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                audits.Add(new
                {
                    DateTimeCreated =
                        reader.IsDBNull(0)
                            ? (DateTime?)null
                            : reader.GetDateTime(0),

                    UpdatedByUserID =
                        reader.IsDBNull(1)
                            ? (int?)null
                            : reader.GetInt32(1),

                    Payload =
                        reader.IsDBNull(2)
                            ? null
                            : reader.GetString(2)
                });
            }

            var response =
                req.CreateResponse(
                    HttpStatusCode.OK);

            await response.WriteAsJsonAsync(
                audits);

            return response;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error loading audit history for donation {DonationID} in organization {OrgID}.",
                donationID,
                orgID);

            var error =
                req.CreateResponse(
                    HttpStatusCode.InternalServerError);

            await error.WriteAsJsonAsync(new
            {
                message =
                    "Unable to load donation history."
            });

            return error;
        }
    }
}