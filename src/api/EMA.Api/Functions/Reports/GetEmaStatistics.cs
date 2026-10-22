using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace EMA_Api.Functions.Reports;

/// <summary>
/// GET /api/statistics — cross-organization statistics cross-tab for the EMA Statistics
/// support screen. Rows are grouped by Classification (Organization or ChMS); columns are
/// time-period buckets (Week/Month/Quarter/Year), capped to the most recent 13. The cell
/// value is the chosen Measurement, scoped by Scope. Reads dbo.vw_EmaStatistics (all orgs).
/// </summary>
public class GetEmaStatistics
{
    private readonly ILogger<GetEmaStatistics> _logger;
    private readonly string _connectionString;

    private const int MaxPeriods = 13;

    public GetEmaStatistics(ILogger<GetEmaStatistics> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    private static readonly HashSet<string> Periods = new(StringComparer.OrdinalIgnoreCase)
        { "Week", "Month", "Quarter", "Year" };

    private static readonly HashSet<string> Classifications = new(StringComparer.OrdinalIgnoreCase)
        { "Organization", "ChMS" };

    // Scope is only meaningful for the Count measurement.
    private static readonly HashSet<string> Scopes = new(StringComparer.OrdinalIgnoreCase)
        { "Organizations", "Donors", "Donations" };

    private static readonly HashSet<string> Measurements = new(StringComparer.OrdinalIgnoreCase)
        { "Count", "ValueEtransfers", "ValueAllDonations", "PercentEtransfersByValue", "PercentEtransfersByNumber" };

    [Function("GetEmaStatistics")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "statistics")] HttpRequestData req)
    {
        _logger.LogInformation("GetEmaStatistics triggered.");


        var query = QueryHelpers.ParseQuery(req.Url.Query);

        if (!TryReadDate(query, "startDate", out var startDate))
            return await CreateBadRequest(req, "Missing or invalid startDate. Use yyyy-MM-dd.");

        if (!TryReadDate(query, "endDate", out var endDate))
            return await CreateBadRequest(req, "Missing or invalid endDate. Use yyyy-MM-dd.");

        if (endDate < startDate)
            return await CreateBadRequest(req, "endDate must be on or after startDate.");

        if (endDate.DayNumber - startDate.DayNumber > 3653)
            return await CreateBadRequest(req, "The statistics date range cannot exceed 10 years.");

        var timePeriod = ReadString(query, "timePeriod") ?? "Year";
        if (!Periods.Contains(timePeriod))
            return await CreateBadRequest(req, "Invalid timePeriod. Use Week, Month, Quarter, or Year.");

        var classification = ReadString(query, "classification") ?? "Organization";
        if (!Classifications.Contains(classification))
            return await CreateBadRequest(req, "Invalid classification. Use Organization or ChMS.");

        var scope = ReadString(query, "scope") ?? "Donations";
        if (!Scopes.Contains(scope))
            return await CreateBadRequest(req, "Invalid scope. Use Organizations, Donors, or Donations.");

        var measurement = ReadString(query, "measurement") ?? "Count";
        if (!Measurements.Contains(measurement))
            return await CreateBadRequest(req, "Invalid measurement.");

        var byChMS = string.Equals(classification, "ChMS", StringComparison.OrdinalIgnoreCase);

        try
        {
            // group -> periodEnd -> accumulated cell
            var byGroup = new Dictionary<string, Dictionary<DateOnly, Cell>>(StringComparer.CurrentCultureIgnoreCase);

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                using var cmd = new SqlCommand(
                    @"SELECT OrganizationID, OrganizationName, ChMSID, ChMSName, EMADonorID,
                             PeriodDate, DonationAmount, IsEtransfer
                      FROM dbo.vw_EmaStatistics
                      WHERE PeriodDate BETWEEN @StartDate AND @EndDate",
                    conn);
                cmd.Parameters.AddWithValue("@StartDate", startDate.ToDateTime(TimeOnly.MinValue));
                cmd.Parameters.AddWithValue("@EndDate", endDate.ToDateTime(TimeOnly.MinValue));

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    if (reader.IsDBNull(5)) continue; // PeriodDate

                    int orgID = reader.GetInt32(0);
                    string orgName = reader.IsDBNull(1) ? "(unknown org)" : reader.GetString(1);
                    string chmsName = reader.IsDBNull(3) ? "(no ChMS)" : reader.GetString(3);
                    int? donorID = reader.IsDBNull(4) ? null : reader.GetInt32(4);
                    var day = DateOnly.FromDateTime(reader.GetDateTime(5));
                    decimal amount = reader.IsDBNull(6) ? 0m : reader.GetDecimal(6);
                    bool isEtransfer = !reader.IsDBNull(7) && reader.GetInt32(7) == 1;

                    var group = byChMS ? chmsName : orgName;
                    var periodEnd = PeriodEnd(day, timePeriod);

                    if (!byGroup.TryGetValue(group, out var periods))
                    {
                        periods = new Dictionary<DateOnly, Cell>();
                        byGroup[group] = periods;
                    }
                    if (!periods.TryGetValue(periodEnd, out var cell))
                    {
                        cell = new Cell();
                        periods[periodEnd] = cell;
                    }

                    cell.SumAll += amount;
                    cell.CountAll += 1;
                    if (isEtransfer) { cell.SumEtransfer += amount; cell.CountEtransfer += 1; }
                    if (donorID is int d) cell.Donors.Add(d);
                    cell.Orgs.Add(orgID);
                }
            }

            // Columns = the most recent 13 period-ends present in the data, newest first.
            var periodEnds = byGroup.Values
                .SelectMany(p => p.Keys)
                .Distinct()
                .OrderByDescending(d => d)
                .Take(MaxPeriods)
                .ToList();

            var periodLabels = periodEnds.Select(pe => PeriodLabel(pe, timePeriod)).ToList();

            // Rows = groups (alphabetical default; the UI re-sorts on column click).
            var rows = new List<object>();
            foreach (var group in byGroup.Keys.OrderBy(g => g, StringComparer.CurrentCultureIgnoreCase))
            {
                var periods = byGroup[group];
                var values = new Dictionary<string, decimal>();

                for (int i = 0; i < periodEnds.Count; i++)
                {
                    var cell = periods.TryGetValue(periodEnds[i], out var c) ? c : null;
                    values[periodLabels[i]] = cell is null ? 0m : MeasurementValue(measurement, scope, cell);
                }

                rows.Add(new { group, values });
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                periods = periodLabels,
                rows
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching EMA statistics.");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            return errorResponse;
        }
    }

    private sealed class Cell
    {
        public decimal SumAll;
        public decimal SumEtransfer;
        public int CountAll;
        public int CountEtransfer;
        public readonly HashSet<int> Donors = new();
        public readonly HashSet<int> Orgs = new();
    }

    private static decimal MeasurementValue(string measurement, string scope, Cell cell) => measurement switch
    {
        "ValueEtransfers" => cell.SumEtransfer,
        "ValueAllDonations" => cell.SumAll,
        "PercentEtransfersByValue" => cell.SumAll > 0m
            ? Math.Round(cell.SumEtransfer / cell.SumAll * 100m, 1) : 0m,
        "PercentEtransfersByNumber" => cell.CountAll > 0
            ? Math.Round((decimal)cell.CountEtransfer / cell.CountAll * 100m, 1) : 0m,
        // Count — entity count (with >= 1 donation in the period) per Scope
        _ => scope switch
        {
            "Organizations" => cell.Orgs.Count,
            "Donors" => cell.Donors.Count,
            _ => cell.CountAll, // Donations
        }
    };

    // The period's ENDING date. Week ends Sunday.
    private static DateOnly PeriodEnd(DateOnly day, string timePeriod) => timePeriod.ToLowerInvariant() switch
    {
        "week" => day.AddDays((7 - (int)day.DayOfWeek) % 7),
        "month" => new DateOnly(day.Year, day.Month, DateTime.DaysInMonth(day.Year, day.Month)),
        "quarter" => QuarterEnd(day),
        _ => new DateOnly(day.Year, 12, 31), // Year
    };

    private static DateOnly QuarterEnd(DateOnly day)
    {
        var endMonth = ((day.Month - 1) / 3) * 3 + 3; // 3,6,9,12
        return new DateOnly(day.Year, endMonth, DateTime.DaysInMonth(day.Year, endMonth));
    }

    // Column header label for a period-end, matching the chosen time period.
    private static string PeriodLabel(DateOnly periodEnd, string timePeriod) => timePeriod.ToLowerInvariant() switch
    {
        "year" => periodEnd.Year.ToString(CultureInfo.InvariantCulture),
        "quarter" => $"Q{(periodEnd.Month - 1) / 3 + 1} {periodEnd.Year}",
        "month" => periodEnd.ToString("MMM yyyy", CultureInfo.InvariantCulture),
        // Week: ISO period-ending date. The client re-formats it with the org's DateFormatID.
        _ => periodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
    };

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
