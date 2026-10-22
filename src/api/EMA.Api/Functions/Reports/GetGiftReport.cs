using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace EMA_Api.Functions.Reports;

public class GetGiftReport
{
    private readonly ILogger<GetGiftReport> _logger;
    private readonly string _connectionString;

    public GetGiftReport(ILogger<GetGiftReport> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    // classification token -> view name (whitelist; safe to interpolate).
    private static readonly Dictionary<string, string> Views = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Fund",        "vw_GiftReport_ByFund" },
        { "Funds",       "vw_GiftReport_ByFund" },
        { "Method",      "vw_GiftReport_ByMethod" },
        { "Campus",      "vw_GiftReport_ByCampus" },
        { "Event",       "vw_GiftReport_ByEvent" },
        { "Source",      "vw_GiftReport_BySource" },
        { "TributeType", "vw_GiftReport_ByTributeType" },
    };

    private static readonly HashSet<string> Periods = new(StringComparer.OrdinalIgnoreCase)
        { "Day", "Week", "Month", "Quarter", "Year" };

    private static readonly HashSet<string> Metrics = new(StringComparer.OrdinalIgnoreCase)
        { "Dollars", "Count", "Average" };

    [Function("GetGiftReport")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "reports/gifts/{orgID:int}")] HttpRequestData req,
        int orgID)
    {
        _logger.LogInformation("GetGiftReport triggered for org {OrgID}.", orgID);


        var query = QueryHelpers.ParseQuery(req.Url.Query);

        if (!TryReadDate(query, "startDate", out var startDate))
            return await CreateBadRequest(req, "Missing or invalid startDate. Use yyyy-MM-dd.");

        if (!TryReadDate(query, "endDate", out var endDate))
            return await CreateBadRequest(req, "Missing or invalid endDate. Use yyyy-MM-dd.");

        if (endDate < startDate)
            return await CreateBadRequest(req, "endDate must be on or after startDate.");

        var classification = ReadString(query, "classification");
        if (string.IsNullOrWhiteSpace(classification) || !Views.TryGetValue(classification, out var viewName))
            return await CreateBadRequest(req, "Invalid classification. Use Fund, Method, Campus, Event, Source, or TributeType.");

        var timePeriod = ReadString(query, "timePeriod");
        if (string.IsNullOrWhiteSpace(timePeriod)) timePeriod = "Day";
        if (!Periods.Contains(timePeriod))
            return await CreateBadRequest(req, "Invalid timePeriod. Use Day, Week, Month, Quarter, or Year.");

        var metric = ReadString(query, "metric");
        if (string.IsNullOrWhiteSpace(metric)) metric = "Dollars";
        if (!Metrics.Contains(metric))
            return await CreateBadRequest(req, "Invalid metric. Use Dollars, Count, or Average.");
        metric = metric.ToLowerInvariant();

        try
        {
            // periodEnd -> classification -> (sum dollars, sum count) accumulated from the daily view rows.
            var byPeriod = new Dictionary<DateOnly, Dictionary<string, (decimal Dollars, decimal Count)>>();
            var classifications = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                // viewName is from the whitelist above — safe to interpolate.
                using var cmd = new SqlCommand(
                    $@"SELECT PeriodDate, ClassificationName, TotalDonations, DonationCount
                       FROM {viewName}
                       WHERE OrganizationID = @OrgID
                         AND PeriodDate BETWEEN @StartDate AND @EndDate",
                    conn);
                cmd.Parameters.AddWithValue("@OrgID", orgID);
                cmd.Parameters.AddWithValue("@StartDate", startDate.ToDateTime(TimeOnly.MinValue));
                cmd.Parameters.AddWithValue("@EndDate", endDate.ToDateTime(TimeOnly.MinValue));

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    // Skip rows with no period date (defensive — PeriodDate is a GROUP BY key, but
                    // a null DateTimeReceived would surface here and otherwise throw).
                    if (reader.IsDBNull(0))
                    {
                        continue;
                    }

                    var day = DateOnly.FromDateTime(reader.GetDateTime(0));
                    var cls = reader.IsDBNull(1) ? "(none)" : reader.GetString(1);
                    var dollars = reader.IsDBNull(2) ? 0m : reader.GetDecimal(2);
                    var count = reader.IsDBNull(3) ? 0m : reader.GetInt32(3);

                    var periodEnd = PeriodEnd(day, timePeriod);
                    if (!byPeriod.TryGetValue(periodEnd, out var row))
                    {
                        row = new Dictionary<string, (decimal, decimal)>();
                        byPeriod[periodEnd] = row;
                    }
                    var prev = row.TryGetValue(cls, out var agg) ? agg : (Dollars: 0m, Count: 0m);
                    row[cls] = (prev.Dollars + dollars, prev.Count + count);
                    classifications.Add(cls);
                }
            }

            var classList = classifications.ToList();
            var periodEnds = byPeriod.Keys.OrderBy(d => d).ToList();

            decimal grandDollars = 0m, grandCount = 0m;
            var rows = new List<object>();

            foreach (var pe in periodEnds)
            {
                var cells = byPeriod[pe];
                var values = new Dictionary<string, decimal>();
                decimal rowDollars = 0m, rowCount = 0m;

                foreach (var cls in classList)
                {
                    var (dollars, count) = cells.TryGetValue(cls, out var a) ? a : (0m, 0m);
                    rowDollars += dollars;
                    rowCount += count;
                    values[cls] = MetricValue(metric, dollars, count);
                }

                grandDollars += rowDollars;
                grandCount += rowCount;

                rows.Add(new
                {
                    // ISO so the client can parse and re-format using the org's DateFormatID.
                    periodDate = pe.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    total = MetricValue(metric, rowDollars, rowCount),
                    values
                });
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                periods = periodEnds.Select(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).ToList(),
                classifications = classList,
                rows,
                grandTotal = MetricValue(metric, grandDollars, grandCount)
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching gift report for org {OrgID}.", orgID);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            return errorResponse;
        }
    }

    // Dollars = sum; Count = sum; Average = weighted (sum$/sumCount) so bucketing >Day stays correct.
    private static decimal MetricValue(string metric, decimal dollars, decimal count) => metric switch
    {
        "count" => count,
        "average" => count > 0m ? dollars / count : 0m,
        _ => dollars
    };

    // The period's ENDING date (matches the "Period Ending" column). Week ends Sunday.
    private static DateOnly PeriodEnd(DateOnly day, string timePeriod) => timePeriod.ToLowerInvariant() switch
    {
        "week" => day.AddDays((7 - (int)day.DayOfWeek) % 7),
        "month" => new DateOnly(day.Year, day.Month, DateTime.DaysInMonth(day.Year, day.Month)),
        "quarter" => QuarterEnd(day),
        "year" => new DateOnly(day.Year, 12, 31),
        _ => day, // Day
    };

    private static DateOnly QuarterEnd(DateOnly day)
    {
        var endMonth = ((day.Month - 1) / 3) * 3 + 3; // 3,6,9,12
        return new DateOnly(day.Year, endMonth, DateTime.DaysInMonth(day.Year, endMonth));
    }

    private static string? ReadString(IReadOnlyDictionary<string, StringValues> query, string key)
        => query.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw) ? raw.ToString().Trim() : null;

    private static bool TryReadDate(IReadOnlyDictionary<string, StringValues> query, string key, out DateOnly value)
    {
        value = default;
        if (!query.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
            return false;
        return DateOnly.TryParseExact(raw.ToString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    private static async Task<HttpResponseData> CreateBadRequest(HttpRequestData req, string message)
    {
        var response = req.CreateResponse(HttpStatusCode.BadRequest);
        await response.WriteAsJsonAsync(new { message });
        return response;
    }
}
