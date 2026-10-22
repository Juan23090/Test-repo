using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace EMA_Api.Functions.Reports;

public class GetJournalEntryDetails
{
    private readonly ILogger<GetJournalEntryDetails> _logger;
    private readonly string _connectionString;

    public GetJournalEntryDetails(ILogger<GetJournalEntryDetails> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("GetJournalEntryDetails")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "reports/journal-entries/{orgID:int}")] HttpRequestData req,
        int orgID)
    {
        _logger.LogInformation("GetJournalEntryDetails triggered for org {OrgID}.", orgID);


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

            var groupedRows = rows
                .GroupBy(r => jeByDate ? r.JournalDate ?? endDate : endDate)
                .OrderBy(g => g.Key)
                .ToList();

            var journals = groupedRows.Select(group =>
            {
                var entries = group
                    .OrderBy(
                        row => sortBy == "account" ? row.Account : row.Description,
                        StringComparer.OrdinalIgnoreCase)
                    .ThenBy(row => row.Account, StringComparer.OrdinalIgnoreCase)
                    .Select(row => new
                    {
                        Account = row.Account,
                        Debits = row.Debits,
                        Credits = row.Credits,
                        Description = row.Description
                    })
                    .ToList();

                var totalDebits = group.Sum(row => row.Debits ?? 0m);
                var totalCredits = group.Sum(row => row.Credits ?? 0m);

                return new
                {
                    JournalDate = group.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    JournalNo = $"EMA {group.Key:yyyy-MM-dd}",
                    Entries = entries,
                    TotalDebits = totalDebits,
                    TotalCredits = totalCredits
                };
            }).ToList();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                EMAOrganizationID = orgID,
                StartDate = startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                EndDate = endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                SortBy = sortBy,
                JeByDate = jeByDate,
                Journals = journals,
                TotalDebits = journals.Sum(j => j.TotalDebits),
                TotalCredits = journals.Sum(j => j.TotalCredits)
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching journal entry details for org {OrgID}.", orgID);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            return errorResponse;
        }
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

    private sealed class JournalEntryRow
    {
        public DateOnly? JournalDate { get; set; }
        public string Account { get; set; } = string.Empty;
        public decimal? Debits { get; set; }
        public decimal? Credits { get; set; }
        public string Description { get; set; } = string.Empty;
    }
}
