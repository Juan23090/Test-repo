using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA_Api.Functions.Reports;

public class GetChequeReport
{
    private readonly ILogger<GetChequeReport> _logger;
    private readonly string _connectionString;

    public GetChequeReport(ILogger<GetChequeReport> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("GetChequeReport")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "reports/cheques/{orgID:int}/{inputDate}")] HttpRequest req,
        int orgID,
        string inputDate)
    {
        _logger.LogInformation("GetChequeReport triggered for org {OrgID} and date {InputDate}.", orgID, inputDate);

        if (!DateTime.TryParseExact(inputDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            return new BadRequestObjectResult("inputDate must be provided in yyyy-MM-dd format.");

        var results = new List<Dictionary<string, object?>>();
        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            // Cheques: pulled by the org's designated Cheque method of giving (not a hardcoded name).
            // Total Cash is derived on the client from the manual Bills/Coin count, so it is not queried here.
            using var cmd = new SqlCommand(
                @"SELECT
                    d.OfficialDonorName AS Name,
                    don.ReferenceNumber AS [#],
                    don.DonationAmount AS Amount
                  FROM Donation don
                  INNER JOIN Donor d ON d.EMADonorID = don.EMADonorID
                  WHERE don.EMAMethodOfGivingID = (
                        SELECT EMAMethodOfGivingIDForCheques
                        FROM Organization
                        WHERE EMAOrganizationID = @EMAOrganizationID
                    )
                    AND don.EMAOrganizationID = @EMAOrganizationID
                    AND CAST(don.DateTimeReceived AS DATE) = @InputDate
                  ORDER BY don.DonationAmount DESC, don.ReferenceNumber ASC, d.OfficialDonorName ASC",
                conn);

            cmd.Parameters.AddWithValue("@EMAOrganizationID", orgID);
            cmd.Parameters.AddWithValue("@InputDate", parsedDate.Date);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                results.Add(row);
            }

            return new OkObjectResult(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching cheque report rows for org {OrgID} and date {InputDate}.", orgID, inputDate);
            return new StatusCodeResult(500);
        }
    }
}
