using System.Globalization;
using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA_Api.Functions.Reports;

// Lists the official year-end receipts already issued to a single donor, so a signed-in donor
// can view and re-print their own tax receipts from the donor portal.
public class GetDonorReceipts
{
    private readonly ILogger<GetDonorReceipts> _logger;
    private readonly string _connectionString;

    public GetDonorReceipts(ILogger<GetDonorReceipts> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("GetDonorReceipts")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "reports/year-end-receipt/donor/{orgID:int}/{donorID:int}")] HttpRequestData req,
        int orgID,
        int donorID)
    {
        if (orgID <= 0 || donorID <= 0)
        {
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteAsJsonAsync(new { message = "A positive orgID and donorID are required." });
            return badRequest;
        }

        try
        {
            var receipts = new List<DonorReceiptSummary>();

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(
                @"SELECT EMAReceiptID, ForTheYearEnding, DateIssued, TotalAmount
                  FROM dbo.DonationReceipt
                  WHERE EMAOrganizationID = @OrgID AND EMADonorID = @DonorID
                  ORDER BY ForTheYearEnding DESC, EMAReceiptID DESC;",
                conn);
            cmd.Parameters.AddWithValue("@OrgID", orgID);
            cmd.Parameters.AddWithValue("@DonorID", donorID);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var yearEnding = reader.GetDateTime(1);
                receipts.Add(new DonorReceiptSummary
                {
                    ReceiptID = reader.GetInt64(0),
                    YearEnding = yearEnding.Year,
                    YearEndingDate = yearEnding.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    DateIssued = reader.GetDateTime(2).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    TotalAmount = reader.GetDecimal(3)
                });
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(receipts);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading receipts for donor {DonorID} in organization {OrgID}.", donorID, orgID);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { message = "Could not load donor receipts." });
            return errorResponse;
        }
    }

    private sealed class DonorReceiptSummary
    {
        public long ReceiptID { get; set; }
        public int YearEnding { get; set; }
        public string YearEndingDate { get; set; } = string.Empty;
        public string DateIssued { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
    }
}
