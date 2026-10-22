using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA_Api.Functions.Reports;

// Records official year-end tax receipts: for each donor with outstanding (un-receipted, non-template)
// donations in the date range, inserts one dbo.DonationReceipt row (EMAReceiptID identity = the receipt
// number) and stamps that number onto the covered dbo.Donation rows. Idempotent: a donor who already has
// a receipt for the year is skipped, and already-stamped donations are never re-numbered. Transactional.
public class PostAssignYearEndReceipts
{
    private readonly ILogger<PostAssignYearEndReceipts> _logger;
    private readonly string _connectionString;

    public PostAssignYearEndReceipts(ILogger<PostAssignYearEndReceipts> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("PostAssignYearEndReceipts")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "reports/year-end-receipt/assign")] HttpRequestData req)
    {
        AssignRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<AssignRequest>(
                req.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return await BadRequest(req, "Request body must be valid JSON.");
        }

        if (request is null)
            return await BadRequest(req, "Request body is required.");

        if (request.EMAOrganizationID <= 0)
            return await BadRequest(req, "'EMAOrganizationID' is required and must be a positive integer.");

        if (!DateTime.TryParseExact(request.StartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startDate))
            return await BadRequest(req, "'startDate' is required and must be in yyyy-MM-dd format.");

        if (!DateTime.TryParseExact(request.EndDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var endDate))
            return await BadRequest(req, "'endDate' is required and must be in yyyy-MM-dd format.");

        if (endDate.Date < startDate.Date)
            return await BadRequest(req, "'endDate' must be greater than or equal to 'startDate'.");

        // CRA-mandatory org details must be on file before official receipts can be issued.
        var missing = await MissingOfficialReceiptRequirementsAsync(request.EMAOrganizationID);
        if (missing.Count > 0)
            return await BadRequest(req, $"Cannot issue official receipts until the organization provides: {string.Join(", ", missing)}. Set these in Organization Info — the CRA requires them on official donation receipts.");

        // Optional filter: the donors the user is issuing OFFICIAL receipts for. Empty = all eligible donors.
        var donorIDs = request.DonorIDs is null
            ? string.Empty
            : string.Join(',', request.DonorIDs.Where(id => id > 0));

        try
        {
            var assigned = await AssignAsync(request.EMAOrganizationID, startDate, endDate, donorIDs);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new AssignResponse
            {
                AssignedCount = assigned.Count,
                Receipts = assigned
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error assigning year-end receipts for organization {OrgID}.", request.EMAOrganizationID);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { message = "Could not assign receipts." });
            return errorResponse;
        }
    }

    private async Task<List<AssignedReceipt>> AssignAsync(int organizationID, DateTime startDate, DateTime endDate, string donorIDs)
    {
        const string sql = @"
SET NOCOUNT ON;
DECLARE @ForTheYearEnding DATETIME = @EndDate;
DECLARE @Now DATETIME = GETDATE();

DECLARE @DonorFilter TABLE (EMADonorID INT PRIMARY KEY);
IF (@EMADonorIDs IS NOT NULL AND LTRIM(RTRIM(@EMADonorIDs)) <> '')
    INSERT INTO @DonorFilter (EMADonorID)
    SELECT DISTINCT TRY_CAST(value AS INT)
    FROM STRING_SPLIT(@EMADonorIDs, ',')
    WHERE TRY_CAST(value AS INT) IS NOT NULL;

DECLARE @HasFilter BIT = CASE WHEN EXISTS (SELECT 1 FROM @DonorFilter) THEN 1 ELSE 0 END;

DECLARE @Eligible TABLE (EMADonorID INT PRIMARY KEY, TotalAmount MONEY);
DECLARE @NewReceipts TABLE (EMAReceiptID BIGINT, EMADonorID INT);

BEGIN TRY
    BEGIN TRAN;

    -- Donors with outstanding gifts in range and no receipt yet for this year.
    INSERT INTO @Eligible (EMADonorID, TotalAmount)
    SELECT don.EMADonorID, SUM(don.DonationAmount)
    FROM dbo.Donation don
    WHERE don.EMAOrganizationID = @EMAOrganizationID
      AND don.EMAReceiptID IS NULL
      AND ISNULL(don.IsTemplate, 'N') <> 'Y'
      AND don.DonationAmount IS NOT NULL
      AND CAST(don.DateTimeReceived AS DATE) BETWEEN @StartDate AND @EndDate
      AND (@HasFilter = 0 OR don.EMADonorID IN (SELECT EMADonorID FROM @DonorFilter))
      AND NOT EXISTS (
            SELECT 1 FROM dbo.DonationReceipt r
            WHERE r.EMAOrganizationID = @EMAOrganizationID
              AND r.EMADonorID = don.EMADonorID
              AND r.ForTheYearEnding = @ForTheYearEnding)
    GROUP BY don.EMADonorID
    HAVING SUM(don.DonationAmount) > 0;

    -- One receipt per eligible donor; capture the generated identity (the receipt number).
    INSERT INTO dbo.DonationReceipt (EMAOrganizationID, EMADonorID, DateIssued, ForTheYearEnding, TotalAmount)
    OUTPUT inserted.EMAReceiptID, inserted.EMADonorID INTO @NewReceipts (EMAReceiptID, EMADonorID)
    SELECT @EMAOrganizationID, e.EMADonorID, @Now, @ForTheYearEnding, e.TotalAmount
    FROM @Eligible e;

    -- Stamp the receipt number onto that donor's covered donations.
    UPDATE don
    SET don.EMAReceiptID = nr.EMAReceiptID
    FROM dbo.Donation don
    INNER JOIN @NewReceipts nr ON nr.EMADonorID = don.EMADonorID
    WHERE don.EMAOrganizationID = @EMAOrganizationID
      AND don.EMAReceiptID IS NULL
      AND ISNULL(don.IsTemplate, 'N') <> 'Y'
      AND CAST(don.DateTimeReceived AS DATE) BETWEEN @StartDate AND @EndDate;

    COMMIT;

    SELECT nr.EMAReceiptID, nr.EMADonorID, e.TotalAmount
    FROM @NewReceipts nr
    INNER JOIN @Eligible e ON e.EMADonorID = nr.EMADonorID
    ORDER BY nr.EMAReceiptID;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH";

        var assigned = new List<AssignedReceipt>();

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@EMAOrganizationID", organizationID);
        cmd.Parameters.AddWithValue("@StartDate", startDate.Date);
        cmd.Parameters.AddWithValue("@EndDate", endDate.Date);
        cmd.Parameters.AddWithValue("@EMADonorIDs", string.IsNullOrWhiteSpace(donorIDs) ? DBNull.Value : donorIDs);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            assigned.Add(new AssignedReceipt
            {
                ReceiptID = reader.GetInt64(0),
                DonorID = reader.GetInt32(1),
                TotalAmount = reader.GetDecimal(2)
            });
        }

        return assigned;
    }

    // CRA-mandatory org-level details that must be on file before official receipts may be
    // issued: the issuing locality (City), the CRA registration number, and an authorized
    // receipt signature. Returns the list of any that are missing (empty = good to go).
    private async Task<List<string>> MissingOfficialReceiptRequirementsAsync(int organizationID)
    {
        var missing = new List<string>();

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(
            @"SELECT City, CRARegisteredCharityNumber, DonationReceiptOfficialSignatoryGraphicBlobID
                FROM dbo.Organization WHERE EMAOrganizationID = @OrgID;", conn);
        cmd.Parameters.AddWithValue("@OrgID", organizationID);

        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            missing.Add("organization record");
            return missing;
        }

        if (reader.IsDBNull(0) || string.IsNullOrWhiteSpace(reader.GetString(0)))
            missing.Add("city (issuing location)");
        if (reader.IsDBNull(1) || string.IsNullOrWhiteSpace(reader.GetString(1)))
            missing.Add("CRA registration number");
        if (reader.IsDBNull(2) || string.IsNullOrWhiteSpace(reader.GetString(2)))
            missing.Add("authorized receipt signature");

        return missing;
    }

    private static async Task<HttpResponseData> BadRequest(HttpRequestData req, string message)
    {
        var response = req.CreateResponse(HttpStatusCode.BadRequest);
        await response.WriteAsJsonAsync(new { message });
        return response;
    }

    private sealed class AssignRequest
    {
        public int EMAOrganizationID { get; set; }
        public string StartDate { get; set; } = string.Empty;
        public string EndDate { get; set; } = string.Empty;
        public List<int>? DonorIDs { get; set; }
    }

    private sealed class AssignResponse
    {
        public int AssignedCount { get; set; }
        public List<AssignedReceipt> Receipts { get; set; } = new();
    }

    private sealed class AssignedReceipt
    {
        public long ReceiptID { get; set; }
        public int DonorID { get; set; }
        public decimal TotalAmount { get; set; }
    }
}
