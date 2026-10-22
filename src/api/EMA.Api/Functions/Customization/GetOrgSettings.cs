using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace EMA_Api.Functions.Customization;

public class GetOrgSettings
{
    private readonly ILogger<GetOrgSettings> _logger;
    private readonly string _connectionString;

    public GetOrgSettings(ILogger<GetOrgSettings> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString");
    }

    [Function("GetOrgSettings")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "customization/settings/{orgID:int}")] HttpRequest req,
        int orgID)
    {
        _logger.LogInformation("GetOrgSettings triggered for org {OrgID}.", orgID);
        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = new SqlCommand(
                @"SELECT o.UseCampuses, o.UseEvents, o.UseFunds, o.UseMethodsOfGiving, o.UseSources, o.UseTributeTypes, o.UseAnonymous, o.EMADonorIDForAnonymous,
                         o.MessageAboutAnonymousDonations, o.DateFormatID AS DateFormat, o.AdminEmail,
                         o.EMAMethodOfGivingIDForCreditDebitCards, o.EMACampusIDForCreditDebitCards,
                         o.EMAMethodOfGivingIDForETransfers, o.EMACampusIDForEtransfers,
                         o.EMAMethodOfGivingIDForPADs, o.EMACampusIDForPADs,
                         o.KPIGraphOrganizationLandingPage, o.KPIGraphDonorLandingPage,
                         CAST(CASE WHEN o.UseAnonymous = 'Y' THEN 1 ELSE 0 END AS bit) AS UseAnonymousEnabled,
                         COALESCE(NULLIF(ad.OfficialDonorName, ''), ad.InteracDonorName) AS AnonymousDonorName
                  FROM dbo.Organization AS o
                  LEFT JOIN dbo.Donor AS ad
                      ON ad.EMADonorID = o.EMADonorIDForAnonymous
                     AND ad.EMAOrganizationID = o.EMAOrganizationID
                  WHERE o.EMAOrganizationID = @OrgID",
                conn);
            cmd.Parameters.AddWithValue("@OrgID", orgID);
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                return new OkObjectResult(row);
            }
            return new NotFoundResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching org settings.");
            return new StatusCodeResult(500);
        }
    }
}
