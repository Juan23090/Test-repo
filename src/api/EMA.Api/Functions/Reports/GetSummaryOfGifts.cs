using System.Globalization;
using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA_Api.Functions.Reports;

public class GetSummaryOfGifts
{
    private readonly ILogger<GetSummaryOfGifts> _logger;
    private readonly string _connectionString;

    public GetSummaryOfGifts(ILogger<GetSummaryOfGifts> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("GetSummaryOfGifts")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "reports/summary-of-gifts")] HttpRequestData req)
    {


        if (!TryGetInputs(req, out var EMAOrganizationID, out var donorID, out var startDate, out var endDate, out var validationMessage))
        {
            var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequestResponse.WriteAsJsonAsync(new { message = validationMessage });
            return badRequestResponse;
        }

        _logger.LogInformation(
            "GetSummaryOfGifts triggered for org {OrgID}, donor {DonorID}. Date range: {StartDate} to {EndDate}.",
            EMAOrganizationID,
            donorID,
            startDate.ToString("yyyy-MM-dd"),
            endDate.ToString("yyyy-MM-dd"));

        var rows = new List<Dictionary<string, object?>>();

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(
                @"SELECT
                    CAST(don.DateTimeDonationLocked AS DATE) AS [DATE],
                    f.Name AS FUND,
                    dd.DistributionAmount AS AMOUNT
                  FROM DonationDistribution dd
                  INNER JOIN Donation don ON don.EMADonationID = dd.EMADonationID
                  INNER JOIN OrganizationFund f ON f.EMAFundID = dd.EMAFundID
                INNER JOIN Donor d ON d.EMADonorID = don.EMADonorID
                  WHERE don.EMAOrganizationID = @EMAOrganizationID
                AND don.EMADonorID = @EMADonorID
                  AND CAST(don.DateTimeDonationLocked AS DATE) BETWEEN @StartDate AND @EndDate
                  ORDER BY don.DateTimeDonationLocked ASC, f.Name ASC",
                conn);

            cmd.Parameters.AddWithValue("@EMAOrganizationID", EMAOrganizationID);
            cmd.Parameters.AddWithValue("@EMADonorID", donorID);
            cmd.Parameters.AddWithValue("@StartDate", startDate.Date);
            cmd.Parameters.AddWithValue("@EndDate", endDate.Date);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(new Dictionary<string, object?>
                {
                    ["DATE"] = reader.IsDBNull(0) ? null : reader.GetDateTime(0).ToString("yyyy-MM-dd"),
                    ["FUND"] = reader.IsDBNull(1) ? null : reader.GetString(1),
                    ["AMOUNT"] = reader.IsDBNull(2) ? null : reader.GetDecimal(2)
                });
            }

            var okResponse = req.CreateResponse(HttpStatusCode.OK);
            await okResponse.WriteAsJsonAsync(rows);
            return okResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching summary of gifts.");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            return errorResponse;
        }
    }

    private static bool TryGetInputs(
        HttpRequestData req,
        out int EMAOrganizationID,
        out int donorID,
        out DateTime startDate,
        out DateTime endDate,
        out string validationMessage)
    {
        EMAOrganizationID = 0;
        donorID = 0;
        startDate = default;
        endDate = default;
        validationMessage = string.Empty;

        var query = QueryHelpers.ParseQuery(req.Url.Query);

        var organizationIDRaw = query.TryGetValue("EMAOrganizationID", out var organizationIDValue)
            ? organizationIDValue.ToString()
            : null;
        if (!int.TryParse(organizationIDRaw, out EMAOrganizationID) || EMAOrganizationID <= 0)
        {
            validationMessage = "Query parameter 'EMAOrganizationID' is required and must be a positive integer.";
            return false;
        }

        var donorIDRaw = query.TryGetValue("donorID", out var donorIDValue)
            ? donorIDValue.ToString()
            : null;
        if (!int.TryParse(donorIDRaw, out donorID) || donorID <= 0)
        {
            validationMessage = "Query parameter 'donorID' is required and must be a positive integer.";
            return false;
        }

        var startDateRaw = query.TryGetValue("startDate", out var startDateValue)
            ? startDateValue.ToString()
            : null;
        if (!DateTime.TryParseExact(startDateRaw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out startDate))
        {
            validationMessage = "Query parameter 'startDate' is required and must be in yyyy-MM-dd format.";
            return false;
        }

        var endDateRaw = query.TryGetValue("endDate", out var endDateValue)
            ? endDateValue.ToString()
            : null;
        if (!DateTime.TryParseExact(endDateRaw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out endDate))
        {
            validationMessage = "Query parameter 'endDate' is required and must be in yyyy-MM-dd format.";
            return false;
        }

        if (endDate.Date < startDate.Date)
        {
            validationMessage = "Query parameter 'endDate' must be greater than or equal to 'startDate'.";
            return false;
        }

        if ((endDate.Date - startDate.Date).TotalDays > 366)
        {
            validationMessage =
                "The report date range cannot exceed 366 days.";
            return false;
        }

        return true;
    }
}
