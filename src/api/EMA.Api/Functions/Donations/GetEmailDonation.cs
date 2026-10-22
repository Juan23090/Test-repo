using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace EMA.Api.Functions.Donations;

public class GetEmailDonation
{
    private readonly ILogger<GetEmailDonation> _logger;
    private readonly string _connectionString;

    public GetEmailDonation(ILogger<GetEmailDonation> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString");
    }

    [Function("GetEmailDonation")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "email/donation/{id:long}")] HttpRequest req,
        long id)
    {
        _logger.LogInformation("GetEmailDonation triggered for donation {ID}.", id);
        try
        {
            var organizationID =
                req.GetRequiredApiIdentity()
                    .EMAOrganizationID;

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var result = new Dictionary<string, object?>();

            using var cmd = new SqlCommand(@"
                SELECT
                    d.EMADonationID, d.EMADonorID, d.EMADonationSourceID, d.EMAMethodOfGivingID,
                    d.EMAEventID,
                    d.DonationAmount, d.DateTimeReceived, d.ReferenceNumber,
                    d.Comments, d.TributeText, d.EMATributeTypeID, d.IsTemplate,
                    d.DateTimeCreated, d.DateTimeUpdated,
                    d.InteracDonorName,
                    dn.OfficialDonorName, dn.EmailAddress,
                    dn.Address, dn.City, dn.ProvinceStateCode, dn.PostalCode, dn.PhoneNumber
                FROM Donation d
                INNER JOIN Donor dn
                    ON d.EMADonorID = dn.EMADonorID
                   AND d.EMAOrganizationID = dn.EMAOrganizationID
                WHERE d.EMADonationID = @ID
                  AND d.EMAOrganizationID = @OrgID", conn);
            cmd.Parameters.AddWithValue("@ID", id);
            cmd.Parameters.AddWithValue(
                "@OrgID",
                organizationID);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                for (int i = 0; i < reader.FieldCount; i++)
                    result[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }
            await reader.CloseAsync();

            using var cmd2 = new SqlCommand(@"
                SELECT dd.EMADonationDistributionID, dd.EMACampusID, dd.EMAFundID, dd.DistributionAmount,
                    c.Name AS CampusName, f.Name AS FundName
                FROM DonationDistribution dd
                INNER JOIN Donation d
                    ON d.EMADonationID = dd.EMADonationID
                   AND d.EMAOrganizationID = @OrgID
                LEFT JOIN OrganizationCampus c
                    ON dd.EMACampusID = c.EMACampusID
                   AND c.EMAOrganizationID = @OrgID
                LEFT JOIN OrganizationFund f
                    ON dd.EMAFundID = f.EMAFundID
                   AND f.EMAOrganizationID = @OrgID
                WHERE dd.EMADonationID = @ID", conn);
            cmd2.Parameters.AddWithValue("@ID", id);
            cmd2.Parameters.AddWithValue(
                "@OrgID",
                organizationID);

            var distributions = new List<Dictionary<string, object?>>();
            using var reader2 = await cmd2.ExecuteReaderAsync();
            while (await reader2.ReadAsync())
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader2.FieldCount; i++)
                    row[reader2.GetName(i)] = reader2.IsDBNull(i) ? null : reader2.GetValue(i);
                distributions.Add(row);
            }

            result["Distributions"] = distributions;
            return new OkObjectResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching email donation.");
            return new StatusCodeResult(500);
        }
    }
}
