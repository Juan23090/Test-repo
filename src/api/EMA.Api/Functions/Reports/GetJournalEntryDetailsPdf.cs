using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace EMA_Api.Functions.Reports;

public class GetJournalEntryDetailsPdf
{
    private readonly ILogger<GetJournalEntryDetailsPdf> _logger;
    private readonly string _connectionString;

    public GetJournalEntryDetailsPdf(ILogger<GetJournalEntryDetailsPdf> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("GetJournalEntryDetailsPdf")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "reports/journal-entries/{orgID:int}/pdf")] HttpRequestData req,
        int orgID)
    {
        _logger.LogInformation("GetJournalEntryDetailsPdf triggered for org {OrgID}.", orgID);


        var query = QueryHelpers.ParseQuery(req.Url.Query);

        if (!TryReadDate(query, "startDate", out var startDate))
        {
            return await CreateBadRequest(req, "Missing or invalid startDate. Use yyyy-MM-dd.");
        }

        if (!TryReadDate(query, "endDate", out var endDate))
        {
            return await CreateBadRequest(req, "Missing or invalid endDate. Use yyyy-MM-dd.");
        }

        if (endDate < startDate)
        {
            return await CreateBadRequest(req, "endDate must be on or after startDate.");
        }

        if (endDate.DayNumber - startDate.DayNumber > 366)
        {
            return await CreateBadRequest(
                req,
                "The report date range cannot exceed 366 days.");
        }

        var sortBy = "description";
        if (query.TryGetValue("sortBy", out var sortByRaw) && !string.IsNullOrWhiteSpace(sortByRaw))
        {
            sortBy = sortByRaw.ToString().Trim().ToLowerInvariant();
            if (sortBy is not ("account" or "description"))
            {
                return await CreateBadRequest(req, "Invalid sortBy. Use account or description.");
            }
        }

        if (!TryReadBool(query, "jeByDate", out var jeByDate))
        {
            return await CreateBadRequest(req, "Invalid jeByDate. Use true/false (or 1/0).");
        }

        try
        {
            var organizationName = await LoadOrganizationNameAsync(orgID);
            var dateFormat = await LoadDateFormatAsync(orgID);
            var accountingSoftwareName = await LoadAccountingSoftwareNameAsync(orgID);
            var rows = await LoadRowsAsync(orgID, startDate, endDate, jeByDate);
            var journals = BuildJournals(rows, sortBy, jeByDate, endDate, dateFormat);
            var totalDebits = journals.Sum(x => x.TotalDebits);
            var totalCredits = journals.Sum(x => x.TotalCredits);

            var documentPayload = new JournalEntryReportDocumentPayload
            {
                EMAOrganizationID = orgID,
                OrganizationName = organizationName,
                AccountingSoftwareName = accountingSoftwareName,
                StartDate = startDate,
                EndDate = endDate,
                SortBy = sortBy,
                JeByDate = jeByDate,
                Journals = journals,
                TotalDebits = totalDebits,
                TotalCredits = totalCredits
            };

            QuestPDF.Settings.License = LicenseType.Community;

            var pdfBytes = new JournalEntryReportDocument(documentPayload).GeneratePdf();
            var fileName = $"journal-entry-details-{startDate:yyyyMMdd}-{endDate:yyyyMMdd}.pdf";

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/pdf");
            response.Headers.Add("Content-Disposition", $"attachment; filename=\"{fileName}\"");
            await response.Body.WriteAsync(pdfBytes);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating journal entry details PDF for org {OrgID}.", orgID);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { message = "Could not generate PDF." });
            return errorResponse;
        }
    }

    private async Task<string> LoadOrganizationNameAsync(int orgID)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = new SqlCommand(
            @"SELECT OrganizationName
                FROM Organization
                WHERE EMAOrganizationID = @OrgID;",
            conn);

        cmd.Parameters.AddWithValue("@OrgID", orgID);

        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return "Organization";
        }

        return reader.IsDBNull(reader.GetOrdinal("OrganizationName"))
            ? "Organization"
            : reader.GetString(reader.GetOrdinal("OrganizationName"));
    }

    private async Task<List<JournalEntryRow>> LoadRowsAsync(int orgID, DateOnly startDate, DateOnly endDate, bool jeByDate)
    {
        var rows = new List<JournalEntryRow>();

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = new SqlCommand(
            @"SELECT
                    CASE WHEN @JeByDate = 1 THEN CAST(don.DateTimeReceived AS date) ELSE NULL END AS JournalDate,
                    mog.GLAccountNumber + ' - ' + mog.Description AS ACCOUNT,
                    SUM(don.DonationAmount) AS DEBITS,
                    CAST(NULL AS decimal(18, 2)) AS CREDITS,
                    mog.Description AS DESCRIPTION
                FROM Donation don
                INNER JOIN OrganizationMethodOfGiving mog
                    ON mog.EMAMethodOfGivingID = don.EMAMethodOfGivingID
                WHERE don.EMAOrganizationID = @EMAOrganizationID
                  AND CAST(don.DateTimeReceived AS date) BETWEEN @StartDate AND @EndDate
                  AND ISNULL(mog.JournalEntryInDetail, 'N') <> 'Y'
                GROUP BY
                    CASE WHEN @JeByDate = 1 THEN CAST(don.DateTimeReceived AS date) ELSE NULL END,
                    mog.GLAccountNumber,
                    mog.Description

                UNION ALL

                SELECT
                    CASE WHEN @JeByDate = 1 THEN CAST(don.DateTimeReceived AS date) ELSE NULL END AS JournalDate,
                    mog.GLAccountNumber + ' - ' + mog.Description AS ACCOUNT,
                    SUM(don.DonationAmount) AS DEBITS,
                    CAST(NULL AS decimal(18, 2)) AS CREDITS,
                    mog.Description AS DESCRIPTION
                FROM Donation don
                INNER JOIN OrganizationMethodOfGiving mog
                    ON mog.EMAMethodOfGivingID = don.EMAMethodOfGivingID
                WHERE don.EMAOrganizationID = @EMAOrganizationID
                  AND CAST(don.DateTimeReceived AS date) BETWEEN @StartDate AND @EndDate
                  AND mog.JournalEntryInDetail = 'Y'
                GROUP BY
                    don.EMADonationID,
                    CASE WHEN @JeByDate = 1 THEN CAST(don.DateTimeReceived AS date) ELSE NULL END,
                    mog.GLAccountNumber,
                    mog.Description

                UNION ALL

                SELECT
                    CASE WHEN @JeByDate = 1 THEN CAST(don.DateTimeReceived AS date) ELSE NULL END AS JournalDate,
                    f.GLAccountNumber + ' - ' + f.Name AS ACCOUNT,
                    CAST(NULL AS decimal(18, 2)) AS DEBITS,
                    SUM(dd.DistributionAmount) AS CREDITS,
                    f.Name AS DESCRIPTION
                FROM DonationDistribution dd
                INNER JOIN OrganizationFund f
                    ON f.EMAFundID = dd.EMAFundID
                INNER JOIN Donation don
                    ON don.EMADonationID = dd.EMADonationID
                WHERE don.EMAOrganizationID = @EMAOrganizationID
                  AND CAST(don.DateTimeReceived AS date) BETWEEN @StartDate AND @EndDate
                GROUP BY
                    CASE WHEN @JeByDate = 1 THEN CAST(don.DateTimeReceived AS date) ELSE NULL END,
                    f.GLAccountNumber,
                    f.Name;",
            conn);

        cmd.Parameters.AddWithValue("@EMAOrganizationID", orgID);
        cmd.Parameters.AddWithValue("@StartDate", startDate.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@EndDate", endDate.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@JeByDate", jeByDate);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            DateOnly? journalDate = null;
            if (!reader.IsDBNull(reader.GetOrdinal("JournalDate")))
            {
                journalDate = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("JournalDate")));
            }

            rows.Add(new JournalEntryRow
            {
                JournalDate = journalDate,
                Account = reader.IsDBNull(reader.GetOrdinal("ACCOUNT"))
                    ? string.Empty
                    : reader.GetString(reader.GetOrdinal("ACCOUNT")),
                Debits = reader.IsDBNull(reader.GetOrdinal("DEBITS"))
                    ? null
                    : reader.GetDecimal(reader.GetOrdinal("DEBITS")),
                Credits = reader.IsDBNull(reader.GetOrdinal("CREDITS"))
                    ? null
                    : reader.GetDecimal(reader.GetOrdinal("CREDITS")),
                Description = reader.IsDBNull(reader.GetOrdinal("DESCRIPTION"))
                    ? string.Empty
                    : reader.GetString(reader.GetOrdinal("DESCRIPTION"))
            });
        }

        return rows;
    }

    private async Task<string?> LoadDateFormatAsync(int orgID)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = new SqlCommand(
            @"SELECT df.DateFormatID AS DateFormat
                FROM Organization o
                LEFT JOIN DateFormat df
                    ON df.DateFormatID = o.DateFormatID
                WHERE o.EMAOrganizationID = @OrgID;",
            conn);

        cmd.Parameters.AddWithValue("@OrgID", orgID);

        var result = await cmd.ExecuteScalarAsync();
        return result is null or DBNull ? null : result.ToString()?.Trim();
    }

    private async Task<string?> LoadAccountingSoftwareNameAsync(int orgID)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = new SqlCommand(
            @"SELECT a.[Name] AS AccountingSoftwareName
                FROM Organization o
                LEFT JOIN AccountingSoftware a
                    ON a.AccountingSoftwareID = o.AccountingSoftwareID
                WHERE o.EMAOrganizationID = @OrgID;",
            conn);

        cmd.Parameters.AddWithValue("@OrgID", orgID);

        var result = await cmd.ExecuteScalarAsync();
        return result is null or DBNull ? null : result.ToString()?.Trim();
    }

    private static string FormatJournalDate(DateOnly date, string pattern)
    {
        try
        {
            return date.ToString(pattern, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
    }

    private static List<JournalEntryGroup> BuildJournals(
        IReadOnlyList<JournalEntryRow> rows,
        string sortBy,
        bool jeByDate,
        DateOnly endDate,
        string? dateFormat)
    {
        var pattern = string.IsNullOrWhiteSpace(dateFormat) ? "yyyy-MM-dd" : dateFormat.Trim();

        return rows
            .GroupBy(r => jeByDate ? r.JournalDate ?? endDate : endDate)
            .OrderBy(g => g.Key)
            .Select(group =>
            {
                var entries = group
                    .OrderBy(
                        row => sortBy == "account" ? row.Account : row.Description,
                        StringComparer.OrdinalIgnoreCase)
                    .ThenBy(row => row.Account, StringComparer.OrdinalIgnoreCase)
                    .Select(row => new JournalEntryItem
                    {
                        Account = row.Account,
                        Debits = row.Debits,
                        Credits = row.Credits,
                        Description = row.Description
                    })
                    .ToList();

                return new JournalEntryGroup
                {
                    JournalDate = FormatJournalDate(group.Key, pattern),
                    JournalNo = $"EMA {group.Key:yyyy-MM-dd}",
                    Entries = entries,
                    TotalDebits = group.Sum(x => x.Debits ?? 0m),
                    TotalCredits = group.Sum(x => x.Credits ?? 0m)
                };
            })
            .ToList();
    }

    private static bool TryReadDate(IReadOnlyDictionary<string, StringValues> query, string key, out DateOnly value)
    {
        value = default;

        if (!query.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        return DateOnly.TryParseExact(
            raw.ToString(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out value);
    }

    private static bool TryReadBool(IReadOnlyDictionary<string, StringValues> query, string key, out bool value)
    {
        value = false;

        if (!query.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        var rawValue = raw.ToString().Trim();
        if (bool.TryParse(rawValue, out value))
        {
            return true;
        }

        if (rawValue == "1")
        {
            value = true;
            return true;
        }

        if (rawValue == "0")
        {
            value = false;
            return true;
        }

        return false;
    }

    private static async Task<HttpResponseData> CreateBadRequest(HttpRequestData req, string message)
    {
        var response = req.CreateResponse(HttpStatusCode.BadRequest);
        await response.WriteAsJsonAsync(new { message });
        return response;
    }

    private sealed class JournalEntryReportDocument : IDocument
    {
        private readonly JournalEntryReportDocumentPayload _payload;

        public JournalEntryReportDocument(JournalEntryReportDocumentPayload payload)
        {
            _payload = payload;
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(16);
                page.DefaultTextStyle(x => x.FontSize(11).FontColor("#202124"));

                page.Content().Column(col =>
                {
                    col.Spacing(12);

                    col.Item().PaddingBottom(4).AlignCenter().Text(_payload.OrganizationName).Bold().FontSize(20);

                    // Mirrors the on-screen heading, including the org's accounting system.
                    col.Item().PaddingBottom(4).AlignCenter().Text(
                        string.IsNullOrWhiteSpace(_payload.AccountingSoftwareName)
                            ? "Automated Journal Entry Creation"
                            : $"Automated Journal Entry Creation - {_payload.AccountingSoftwareName}")
                        .SemiBold().FontSize(13);

                    if (_payload.Journals.Count == 0)
                    {
                        col.Item().Text("No journal entries found for the selected filters.").SemiBold();
                    }

                    foreach (var journal in _payload.Journals)
                    {
                        col.Item().Element(c => ComposeJournalPanel(c, journal));
                    }

                    col.Item().PaddingTop(2).AlignCenter().Text(
                        $"Grand Totals: Debits {FormatAmount(_payload.TotalDebits)} | Credits {FormatAmount(_payload.TotalCredits)}").Bold().FontSize(16);
                });
            });
        }

        private static void ComposeJournalPanel(IContainer container, JournalEntryGroup journal)
        {
            container
                .Background("#dcdcdc")
                .Border(1)
                .BorderColor("#8a8a8a")
                .Column(col =>
            {
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                    });

                    table.Cell().Element(MetaCell).AlignCenter().Text($"Journal Date: {journal.JournalDate}").SemiBold().FontSize(12);
                    table.Cell().Element(MetaCell).AlignCenter().Text($"Journal No: {journal.JournalNo}").SemiBold().FontSize(12);
                });

                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(3f);
                        columns.RelativeColumn(1.1f);
                        columns.RelativeColumn(1.1f);
                        columns.RelativeColumn(2.2f);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Element(ColumnHeaderCell).Text("ACCOUNT").SemiBold();
                        header.Cell().Element(ColumnHeaderCell).AlignCenter().Text("DEBITS").SemiBold();
                        header.Cell().Element(ColumnHeaderCell).AlignCenter().Text("CREDITS").SemiBold();
                        header.Cell().Element(ColumnHeaderCell).Text("DESCRIPTION").SemiBold();
                    });

                    foreach (var entry in journal.Entries)
                    {
                        table.Cell().Element(DataCell).Text(entry.Account).Bold();
                        table.Cell().Element(DataCell).AlignCenter().Text(FormatNullableAmount(entry.Debits)).Bold();
                        table.Cell().Element(DataCell).AlignCenter().Text(FormatNullableAmount(entry.Credits)).Bold();
                        table.Cell().Element(DataCell).Text(entry.Description).Bold();
                    }

                    table.Cell().Element(DataCell).Text(string.Empty);
                    table.Cell().Element(DataCell).AlignCenter().Text(FormatAmount(journal.TotalDebits));
                    table.Cell().Element(DataCell).AlignCenter().Text(FormatAmount(journal.TotalCredits));
                    table.Cell().Element(DataCell).Text(string.Empty);
                });
            });
        }

        private static IContainer MetaCell(IContainer container)
            => container
                .BorderBottom(1)
                .BorderRight(1)
                .BorderColor("#8a8a8a")
                .PaddingVertical(9)
                .PaddingHorizontal(8)
                .Background("#dcdcdc");

        private static IContainer ColumnHeaderCell(IContainer container)
            => container
                .BorderBottom(1)
                .BorderRight(1)
                .BorderColor("#8a8a8a")
                .PaddingVertical(8)
                .PaddingHorizontal(8)
                .Background("#dcdcdc");

        private static IContainer DataCell(IContainer container)
            => container
                .BorderBottom(1)
                .BorderRight(1)
                .BorderColor("#8a8a8a")
                .MinHeight(34)
                .PaddingVertical(7)
                .PaddingHorizontal(8)
                .Background("#dcdcdc");
    }

    private static string FormatAmount(decimal value)
        => value.ToString("$#,##0.00", CultureInfo.InvariantCulture);

    private static string FormatNullableAmount(decimal? value)
        => value.HasValue ? FormatAmount(value.Value) : string.Empty;

    private sealed class JournalEntryReportDocumentPayload
    {
        public int EMAOrganizationID { get; init; }
        public string OrganizationName { get; init; } = string.Empty;
        public string? AccountingSoftwareName { get; init; }
        public DateOnly StartDate { get; init; }
        public DateOnly EndDate { get; init; }
        public string SortBy { get; init; } = string.Empty;
        public bool JeByDate { get; init; }
        public List<JournalEntryGroup> Journals { get; init; } = new();
        public decimal TotalDebits { get; init; }
        public decimal TotalCredits { get; init; }
    }

    private sealed class JournalEntryGroup
    {
        public string JournalDate { get; init; } = string.Empty;
        public string JournalNo { get; init; } = string.Empty;
        public decimal TotalDebits { get; init; }
        public decimal TotalCredits { get; init; }
        public List<JournalEntryItem> Entries { get; init; } = new();
    }

    private sealed class JournalEntryItem
    {
        public string Account { get; init; } = string.Empty;
        public decimal? Debits { get; init; }
        public decimal? Credits { get; init; }
        public string Description { get; init; } = string.Empty;
    }

    private sealed class JournalEntryRow
    {
        public DateOnly? JournalDate { get; set; }
        public string Account { get; set; } = string.Empty;
        public decimal? Debits { get; set; }
        public decimal? Credits { get; set; }
        public string Description { get; set; } = string.Empty;
    }
}
