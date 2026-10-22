using System.Globalization;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading;
using EMA.Api.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA_Api.Functions.Reports;

public class GetYearEndReceiptPreview
{
    private readonly ILogger<GetYearEndReceiptPreview> _logger;
    private readonly string _connectionString;
    private readonly IBlobStorageService _blobStorageService;

    public GetYearEndReceiptPreview(
        ILogger<GetYearEndReceiptPreview> logger,
        IBlobStorageService blobStorageService)
    {
        _logger = logger;
        _blobStorageService = blobStorageService;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("GetYearEndReceiptPreview")] 
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "reports/year-end-receipt/preview")] HttpRequestData req)
    {


        if (!TryGetInputs(req, out var EMAOrganizationID, out var donorID, out var receiptID, out var startDate, out var endDate, out var validationMessage))
        {
            var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequestResponse.WriteAsJsonAsync(new { message = validationMessage });
            return badRequestResponse;
        }

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var organization = await LoadOrganizationAsync(conn, EMAOrganizationID);
            if (organization is null)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteAsJsonAsync(new { message = "Organization not found." });
                return notFoundResponse;
            }

            var donors = await LoadDonorPreviewsAsync(conn, EMAOrganizationID, startDate, endDate, donorID, receiptID);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new YearEndReceiptPreviewBatchResponse
            {
                OrganizationName = organization.Name,
                OrganizationAddressLine = organization.Address,
                OrganizationContactLine = organization.ContactLine,
                OrganizationWebsite = organization.Website,
                OrganizationPhone = organization.Phone,
                OrganizationEmail = organization.Email,
                DateText = DateTime.Today.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture),
                OfficialLetterBody = organization.OfficialLetterBody,
                UnofficialLetterBody = organization.UnofficialLetterBody,
                LetterSignatureGraphic = organization.LetterSignatureGraphic,
                LetterSignatureTitle = organization.LetterSignatureGraphic is null || organization.LetterSignatureGraphic.Length == 0
                    ? string.Empty
                    : organization.LetterSignatureTitle,
                ReceiptSignatureGraphic = organization.ReceiptSignatureGraphic,
                ReceiptSignatureTitle = organization.ReceiptSignatureGraphic is null || organization.ReceiptSignatureGraphic.Length == 0
                    ? string.Empty
                    : organization.ReceiptSignatureTitle,
                ReceiptFooterText = "OFFICIAL RECEIPT FOR INCOME TAX PURPOSES",
                OrganizationRegistrationNumber = organization.RegistrationNumber,
                OrganizationIssuedInLocation = organization.IssuedInLocation,
                Donors = donors.Select(donorPreview => new YearEndReceiptPreviewResponse
                {
                    DonorID = donorPreview.DonorID,
                    RecipientName = donorPreview.RecipientName,
                    DonorAddress = donorPreview.Address,
                    ReceiptNumber = donorPreview.ReceiptID?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    YearReceived = endDate.Year.ToString(CultureInfo.InvariantCulture),
                    DateReceipted = (donorPreview.DateIssued ?? DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    // CRA requires the city/town/municipality where the receipt was issued.
                    // No fallback: if the org hasn't set its city this stays empty so official
                    // generation is blocked rather than showing a non-compliant placeholder.
                    ReceiptIssuedIn = organization.IssuedInLocation,
                    UniqueIdentifier = donorPreview.ReceiptID?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    DonationAmount = donorPreview.Gifts.Sum(x => x.Amount),
                    Gifts = donorPreview.Gifts
                }).ToList()
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating year-end receipt preview.");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            return errorResponse;
        }
    }

    private async Task<OrganizationPreview?> LoadOrganizationAsync(SqlConnection conn, int EMAOrganizationID)
    {
                using var cmd = new SqlCommand(
                        @"SELECT OrganizationName,
                                         Address,
                                         City,
                                         ProvinceStateCode,
                                         PostalCode,
                                         OrganizationPhone,
                                         OrganizationWebsite,
                                         AdminEmail,
                                         DonationReceiptOfficialLetterBody,
                                         DonationReceiptUnofficialLetterBody,
                                         DonationReceiptLetterSignatoryTitle,
                                         DonationReceiptOfficialSignatoryTitle,
                                         DonationReceiptLetterSignatoryGraphicBlobID,
                                         DonationReceiptOfficialSignatoryGraphicBlobID,
                                         CRARegisteredCharityNumber
                            FROM Organization
                            WHERE EMAOrganizationID = @OrgID;",
                        conn);
        cmd.Parameters.AddWithValue("@OrgID", EMAOrganizationID);

        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

            var preview = new OrganizationPreview
        {
            Name = reader.IsDBNull(reader.GetOrdinal("OrganizationName")) ? string.Empty : reader.GetString(reader.GetOrdinal("OrganizationName")),
            Address = string.Join(", ", new[]
            {
                reader.IsDBNull(reader.GetOrdinal("Address")) ? string.Empty : reader.GetString(reader.GetOrdinal("Address")),
                reader.IsDBNull(reader.GetOrdinal("City")) ? string.Empty : reader.GetString(reader.GetOrdinal("City")),
                reader.IsDBNull(reader.GetOrdinal("ProvinceStateCode")) ? string.Empty : reader.GetString(reader.GetOrdinal("ProvinceStateCode")),
                reader.IsDBNull(reader.GetOrdinal("PostalCode")) ? string.Empty : reader.GetString(reader.GetOrdinal("PostalCode"))
            }.Where(value => !string.IsNullOrWhiteSpace(value))),
            Website = reader.IsDBNull(reader.GetOrdinal("OrganizationWebsite")) ? string.Empty : reader.GetString(reader.GetOrdinal("OrganizationWebsite")),
            Phone = reader.IsDBNull(reader.GetOrdinal("OrganizationPhone")) ? string.Empty : reader.GetString(reader.GetOrdinal("OrganizationPhone")),
            Email = reader.IsDBNull(reader.GetOrdinal("AdminEmail")) ? string.Empty : reader.GetString(reader.GetOrdinal("AdminEmail")),
            ContactLine = string.Join(" • ", new[]
            {
                reader.IsDBNull(reader.GetOrdinal("OrganizationWebsite")) ? string.Empty : reader.GetString(reader.GetOrdinal("OrganizationWebsite")),
                reader.IsDBNull(reader.GetOrdinal("OrganizationPhone")) ? string.Empty : reader.GetString(reader.GetOrdinal("OrganizationPhone")),
                reader.IsDBNull(reader.GetOrdinal("AdminEmail")) ? string.Empty : reader.GetString(reader.GetOrdinal("AdminEmail"))
            }.Where(value => !string.IsNullOrWhiteSpace(value))),
            OfficialLetterBody = reader.IsDBNull(reader.GetOrdinal("DonationReceiptOfficialLetterBody")) ? string.Empty : reader.GetString(reader.GetOrdinal("DonationReceiptOfficialLetterBody")),
            UnofficialLetterBody = reader.IsDBNull(reader.GetOrdinal("DonationReceiptUnofficialLetterBody")) ? string.Empty : reader.GetString(reader.GetOrdinal("DonationReceiptUnofficialLetterBody")),
            LetterSignatureTitle = reader.IsDBNull(reader.GetOrdinal("DonationReceiptLetterSignatoryTitle")) ? string.Empty : reader.GetString(reader.GetOrdinal("DonationReceiptLetterSignatoryTitle")),
            ReceiptSignatureTitle = reader.IsDBNull(reader.GetOrdinal("DonationReceiptOfficialSignatoryTitle")) ? string.Empty : reader.GetString(reader.GetOrdinal("DonationReceiptOfficialSignatoryTitle")),
            RegistrationNumber = reader.IsDBNull(reader.GetOrdinal("CRARegisteredCharityNumber")) ? string.Empty : reader.GetString(reader.GetOrdinal("CRARegisteredCharityNumber")).Trim(),
            // CRA "location where the receipt was issued (city, town, municipality)". The CITY is
            // required — province alone is NOT a valid locality — so this is empty unless City is
            // set (empty then blocks official generation upstream). Province is appended only as
            // a qualifier when the city is present.
            IssuedInLocation = BuildIssuedInLocation(
                reader.IsDBNull(reader.GetOrdinal("City")) ? string.Empty : reader.GetString(reader.GetOrdinal("City")),
                reader.IsDBNull(reader.GetOrdinal("ProvinceStateCode")) ? string.Empty : reader.GetString(reader.GetOrdinal("ProvinceStateCode")))
        };

        // Signature graphics moved to blob storage (…GraphicBlobID). Fetch the bytes by BlobID and
        // populate the byte[] the DTO/frontend already expect. Best-effort: blank if the blob is
        // missing/unavailable (e.g. the local storage emulator), so the preview never re-breaks.
        var letterSignatureBlobID = reader.IsDBNull(reader.GetOrdinal("DonationReceiptLetterSignatoryGraphicBlobID"))
            ? null : reader.GetString(reader.GetOrdinal("DonationReceiptLetterSignatoryGraphicBlobID"));
        var receiptSignatureBlobID = reader.IsDBNull(reader.GetOrdinal("DonationReceiptOfficialSignatoryGraphicBlobID"))
            ? null : reader.GetString(reader.GetOrdinal("DonationReceiptOfficialSignatoryGraphicBlobID"));

        // Fetch both signatures in parallel with a short timeout so a missing/slow blob
        // (e.g. the local storage emulator being down) can't stall the whole preview for the
        // Azure SDK's default retry-backoff (~25s each, sequentially). Blank on timeout/failure.
        using var signatureTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var letterTask = TryGetBlobBytesAsync(EMAOrganizationID, letterSignatureBlobID, signatureTimeout.Token);
        var receiptTask = TryGetBlobBytesAsync(EMAOrganizationID, receiptSignatureBlobID, signatureTimeout.Token);
        await Task.WhenAll(letterTask, receiptTask);
        preview.LetterSignatureGraphic = letterTask.Result;
        preview.ReceiptSignatureGraphic = receiptTask.Result;

        return preview;
    }

    // CRA needs the city/town/municipality where the receipt is issued. The city is required —
    // province alone is not a valid locality — so this returns empty unless a city is present,
    // appending the province only as a qualifier. Empty result blocks official generation upstream.
    private static string BuildIssuedInLocation(string city, string province)
    {
        city = city?.Trim() ?? string.Empty;
        province = province?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(city)) return string.Empty;
        return string.IsNullOrWhiteSpace(province) ? city : $"{city}, {province}";
    }

    // Fetches a blob's bytes by id; returns null (blank signature) if it's missing, unavailable, or times out.
    private async Task<byte[]?> TryGetBlobBytesAsync(
        int EMAOrganizationID,
        string? blobID,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(blobID))
            return null;

        try
        {
            BlobGetResult result;

            try
            {
                result =
                    await _blobStorageService
                        .BlobGetAsync(
                            TenantBlobName
                                .ForOrganization(
                                    EMAOrganizationID,
                                    blobID),
                            cancellationToken);
            }
            catch (FileNotFoundException)
            {
                // Legacy signature blobs predate tenant namespacing. This ID
                // came directly from the requested organization's row.
                result =
                    await _blobStorageService
                        .BlobGetAsync(
                            blobID,
                            cancellationToken);
            }

            if (result.ContentLength >
                2L * 1024 * 1024)
            {
                throw new InvalidDataException(
                    "Signature graphic exceeds the supported size.");
            }

            using var memory = new MemoryStream();
            await result.Content.CopyToAsync(memory, cancellationToken);
            return memory.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load signature graphic blob {BlobID}; rendering without it.", blobID);
            return null;
        }
    }

    private async Task<List<DonorPreview>> LoadDonorPreviewsAsync(SqlConnection conn, int EMAOrganizationID, DateTime startDate, DateTime endDate, int? donorID, long? receiptID)
    {
        var donors = new Dictionary<int, DonorPreview>();

        using var cmd = new SqlCommand(
            @"SELECT don.EMADonorID,
                     COALESCE(NULLIF(LTRIM(RTRIM(d.OfficialDonorName)), ''), CONCAT('Donor #', don.EMADonorID)) AS RecipientName,
                     CONCAT_WS(', ', NULLIF(LTRIM(RTRIM(d.Address)), ''), NULLIF(LTRIM(RTRIM(d.City)), ''), NULLIF(LTRIM(RTRIM(d.ProvinceStateCode)), ''), NULLIF(LTRIM(RTRIM(d.PostalCode)), '')) AS DonorAddress,
                     CAST(don.DateTimeReceived AS DATE) AS [DATE],
                     COALESCE(f.Name, '') AS FUND,
                     COALESCE(dd.DistributionAmount, don.DonationAmount) AS AMOUNT,
                     don.EMAReceiptID AS RECEIPTID,
                     rcpt.DateIssued AS DATEISSUED
              -- Drive off Donation (not DonationDistribution) so gifts with no fund allocation
              -- still list and the gift total reconciles with the recorded receipt total.
              FROM Donation don
              INNER JOIN Donor d ON d.EMADonorID = don.EMADonorID AND d.EMAOrganizationID = don.EMAOrganizationID
              LEFT JOIN DonationDistribution dd ON dd.EMADonationID = don.EMADonationID
              LEFT JOIN OrganizationFund f ON f.EMAFundID = dd.EMAFundID
              LEFT JOIN DonationReceipt rcpt ON rcpt.EMAReceiptID = don.EMAReceiptID
              WHERE don.EMAOrganizationID = @EMAOrganizationID
                AND ISNULL(don.IsTemplate, 'N') <> 'Y'
                AND (@EMADonorID IS NULL OR don.EMADonorID = @EMADonorID)
                -- When a receipt id is supplied (donor re-print), return exactly that receipt's
                -- stamped donations regardless of date; otherwise filter by the date range.
                AND (
                      (@ReceiptID IS NOT NULL AND don.EMAReceiptID = @ReceiptID)
                      OR
                      (@ReceiptID IS NULL AND CAST(don.DateTimeReceived AS DATE) BETWEEN @StartDate AND @EndDate)
                    )
              ORDER BY RecipientName ASC, don.EMADonorID ASC, don.DateTimeReceived ASC, f.Name ASC",
            conn);

        cmd.Parameters.AddWithValue("@EMAOrganizationID", EMAOrganizationID);
        cmd.Parameters.AddWithValue("@EMADonorID", donorID is null ? DBNull.Value : donorID.Value);
        cmd.Parameters.AddWithValue("@StartDate", startDate.Date);
        cmd.Parameters.AddWithValue("@EndDate", endDate.Date);
        cmd.Parameters.AddWithValue("@ReceiptID", receiptID is null ? DBNull.Value : receiptID.Value);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var currentDonorID = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
            if (currentDonorID <= 0)
            {
                continue;
            }

            if (!donors.TryGetValue(currentDonorID, out var donor))
            {
                donor = new DonorPreview
                {
                    DonorID = currentDonorID,
                    RecipientName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    Address = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    ReceiptID = reader.IsDBNull(6) ? null : reader.GetInt64(6),
                    DateIssued = reader.IsDBNull(7) ? null : reader.GetDateTime(7)
                };
                donors[currentDonorID] = donor;
            }

            donor.Gifts.Add(new YearEndReceiptGiftRow
            {
                Date = reader.IsDBNull(3) ? string.Empty : reader.GetDateTime(3).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Fund = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                Amount = reader.IsDBNull(5) ? 0m : reader.GetDecimal(5)
            });
        }

        // Print/display in receipt-number order: assigned donors first (ascending EMAReceiptID,
        // per Roger's "select each receipt in DonationReceiptID order"), then the rest by name.
        return donors.Values
            .OrderBy(d => d.ReceiptID.HasValue ? 0 : 1)
            .ThenBy(d => d.ReceiptID ?? long.MaxValue)
            .ThenBy(d => d.RecipientName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool TryGetInputs(
        HttpRequestData req,
        out int EMAOrganizationID,
        out int? donorID,
        out long? receiptID,
        out DateTime startDate,
        out DateTime endDate,
        out string validationMessage)
    {
        EMAOrganizationID = 0;
        donorID = null;
        receiptID = null;
        startDate = default;
        endDate = default;
        validationMessage = string.Empty;

        var query = QueryHelpers.ParseQuery(req.Url.Query);

        var receiptIDRaw = query.TryGetValue("receiptID", out var receiptIDValue) ? receiptIDValue.ToString() : null;
        if (!string.IsNullOrWhiteSpace(receiptIDRaw))
        {
            if (!long.TryParse(receiptIDRaw, out var parsedReceiptID) || parsedReceiptID <= 0)
            {
                validationMessage = "Query parameter 'receiptID' must be a positive integer when provided.";
                return false;
            }

            receiptID = parsedReceiptID;
        }

        var organizationIDRaw = query.TryGetValue("EMAOrganizationID", out var organizationIDValue) ? organizationIDValue.ToString() : null;
        if (!int.TryParse(organizationIDRaw, out EMAOrganizationID) || EMAOrganizationID <= 0)
        {
            validationMessage = "Query parameter 'EMAOrganizationID' is required and must be a positive integer.";
            return false;
        }

        var donorIDRaw = query.TryGetValue("donorID", out var donorIDValue) ? donorIDValue.ToString() : null;
        if (!string.IsNullOrWhiteSpace(donorIDRaw))
        {
            if (!int.TryParse(donorIDRaw, out var parsedDonorID) || parsedDonorID <= 0)
            {
                validationMessage = "Query parameter 'donorID' must be a positive integer when provided.";
                return false;
            }

            donorID = parsedDonorID;
        }

        var startDateRaw = query.TryGetValue("startDate", out var startDateValue) ? startDateValue.ToString() : null;
        if (!DateTime.TryParseExact(startDateRaw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out startDate))
        {
            validationMessage = "Query parameter 'startDate' is required and must be in yyyy-MM-dd format.";
            return false;
        }

        var endDateRaw = query.TryGetValue("endDate", out var endDateValue) ? endDateValue.ToString() : null;
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

    private sealed class OrganizationPreview
    {
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string ContactLine { get; set; } = string.Empty;
        public string Website { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string OfficialLetterBody { get; set; } = string.Empty;
        public string UnofficialLetterBody { get; set; } = string.Empty;
        public byte[]? LetterSignatureGraphic { get; set; }
        public string LetterSignatureTitle { get; set; } = string.Empty;
        public byte[]? ReceiptSignatureGraphic { get; set; }
        public string ReceiptSignatureTitle { get; set; } = string.Empty;
        public string RegistrationNumber { get; set; } = string.Empty;
        public string IssuedInLocation { get; set; } = string.Empty;
    }

    private sealed class DonorPreview
    {
        public int DonorID { get; set; }
        public string RecipientName { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public long? ReceiptID { get; set; }
        public DateTime? DateIssued { get; set; }
        public List<YearEndReceiptGiftRow> Gifts { get; set; } = new();
    }

    private sealed class YearEndReceiptPreviewBatchResponse
    {
        public string OrganizationName { get; set; } = string.Empty;
        public string OrganizationAddressLine { get; set; } = string.Empty;
        public string OrganizationContactLine { get; set; } = string.Empty;
        public string OrganizationWebsite { get; set; } = string.Empty;
        public string OrganizationPhone { get; set; } = string.Empty;
        public string OrganizationEmail { get; set; } = string.Empty;
        public string DateText { get; set; } = string.Empty;
        public string OfficialLetterBody { get; set; } = string.Empty;
        public string UnofficialLetterBody { get; set; } = string.Empty;
        public byte[]? LetterSignatureGraphic { get; set; }
        public string LetterSignatureTitle { get; set; } = string.Empty;
        public byte[]? ReceiptSignatureGraphic { get; set; }
        public string ReceiptSignatureTitle { get; set; } = string.Empty;
        public string ReceiptFooterText { get; set; } = string.Empty;
        public string OrganizationRegistrationNumber { get; set; } = string.Empty;
        public string OrganizationIssuedInLocation { get; set; } = string.Empty;
        public List<YearEndReceiptPreviewResponse> Donors { get; set; } = new();
    }

    private sealed class YearEndReceiptPreviewResponse
    {
        public int DonorID { get; set; }
        public string RecipientName { get; set; } = string.Empty;
        public string DonorAddress { get; set; } = string.Empty;
        public string ReceiptNumber { get; set; } = string.Empty;
        public string YearReceived { get; set; } = string.Empty;
        public string DateReceipted { get; set; } = string.Empty;
        public string ReceiptIssuedIn { get; set; } = string.Empty;
        public string UniqueIdentifier { get; set; } = string.Empty;
        public decimal DonationAmount { get; set; }
        public List<YearEndReceiptGiftRow> Gifts { get; set; } = new();
    }

    private sealed class YearEndReceiptGiftRow
    {
        public string Date { get; set; } = string.Empty;
        public string Fund { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }
}
