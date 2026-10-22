using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace EMA_Api.Functions.Reports;

public class GetJournalEntryDetailsCsv
{
    private readonly ILogger<GetJournalEntryDetailsCsv> _logger;
    private readonly string _connectionString;

    public GetJournalEntryDetailsCsv(ILogger<GetJournalEntryDetailsCsv> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("GetJournalEntryDetailsCsv")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "reports/journal-entries/{orgID:int}/csv")] HttpRequestData req,
        int orgID)
    {
        _logger.LogInformation("GetJournalEntryDetailsCsv triggered for org {OrgID}.", orgID);


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

        if (endDate.DayNumber -
            startDate.DayNumber >
            366)
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
            var dateFormat = await LoadDateFormatAsync(orgID);
            var rows = await LoadRowsAsync(orgID, startDate, endDate, jeByDate);
            var journals = BuildJournals(rows, sortBy, jeByDate, endDate, dateFormat);

            var csv = GenerateCsv(journals);
            var fileName = $"journal-entry-details-{startDate:yyyyMMdd}-{endDate:yyyyMMdd}.csv";

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "text/csv; charset=utf-8");
            response.Headers.Add("Content-Disposition", $"attachment; filename=\"{fileName}\"");
            await response.Body.WriteAsync(Encoding.UTF8.GetBytes(csv));
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating journal entry details CSV for org {OrgID}.", orgID);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { message = "Could not generate CSV." });
            return errorResponse;
        }
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
                   AND mog.EMAOrganizationID = don.EMAOrganizationID
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
                   AND mog.EMAOrganizationID = don.EMAOrganizationID
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
                   AND f.EMAOrganizationID = don.EMAOrganizationID
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

    private static string GenerateCsv(List<JournalEntryGroup> journals)
    {
        var csv = new StringBuilder();
        
        // Write header
        csv.AppendLine("Journal No,Journal Date,Account,Debits,Credits,Description");

        // Write data rows
        foreach (var journal in journals)
        {
            var journalDate = journal.JournalDate;
            
            foreach (var entry in journal.Entries)
            {
                csv.Append(EscapeCsvField(journal.JournalNo));
                csv.Append(",");
                csv.Append(EscapeCsvField(journalDate));
                csv.Append(",");
                csv.Append(EscapeCsvField(entry.Account));
                csv.Append(",");
                csv.Append(FormatCsvAmount(entry.Debits));
                csv.Append(",");
                csv.Append(FormatCsvAmount(entry.Credits));
                csv.Append(",");
                csv.Append(EscapeCsvField(entry.Description));
                csv.AppendLine();
            }

            // Write totals row
            csv.Append(EscapeCsvField(journal.JournalNo));
            csv.Append(",");
            csv.Append(EscapeCsvField(journalDate));
            csv.Append(",");
            csv.Append("\"\""); // Empty account for totals
            csv.Append(",");
            csv.Append(FormatCsvAmount(journal.TotalDebits));
            csv.Append(",");
            csv.Append(FormatCsvAmount(journal.TotalCredits));
            csv.Append(",");
            csv.Append("\"\""); // Empty description for totals
            csv.AppendLine();
        }

        return csv.ToString();
    }

    private static string EscapeCsvField(string field)
    {
        if (string.IsNullOrEmpty(field))
        {
            return "\"\"";
        }

        var firstMeaningfulCharacter =
            field.TrimStart(
                ' ',
                '\t',
                '\r',
                '\n')
                .FirstOrDefault();

        if (firstMeaningfulCharacter is
            '=' or '+' or '-' or '@')
        {
            field =
                "'" + field;
        }

        if (field.Contains("\"") || field.Contains(",") || field.Contains("\n") || field.Contains("\r"))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }

        return field;
    }

    private static string FormatCsvAmount(decimal? value)
    {
        if (!value.HasValue || value == 0m)
        {
            return string.Empty;
        }

        return value.Value.ToString("0.00", CultureInfo.InvariantCulture);
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
