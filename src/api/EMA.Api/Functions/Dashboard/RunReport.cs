using EMA.Api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace EMA.Api.Functions.Dashboard;

/// <summary>
/// POST /api/reports/run — runs a filtered donation report and returns a dynamic grid of
/// Columns + Rows. The Summarize field selects the SQL view from a whitelist; all filter
/// values are fully parameterized — no raw user input is ever interpolated into SQL.
/// EMAOrganizationID is supplied in the request body.
/// </summary>
public class RunReport
{
    private readonly ILogger<RunReport> _logger;
    private readonly string _connectionString;

    /// <summary>
    /// Maps the Summarize filter value to a whitelisted SQL view name and its date column.
    /// Using a whitelist ensures both values can never be injected by a caller.
    /// </summary>
    private static readonly Dictionary<string, (string View, string DateColumn)> SummarizeViewMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { "None",    ("dbo.vw_Donations_Detail",    "DateTimeReceived") },
            { "Day",     ("dbo.vw_Donations_ByDay",     "DonationDate")     },
            { "Week",    ("dbo.vw_Donations_ByWeek",    "WeekStartDate")    },
            { "Month",   ("dbo.vw_Donations_ByMonth",   "MonthStartDate")   },
            { "Quarter", ("dbo.vw_Donations_ByQuarter", "QuarterStartDate") },
            { "Year",    ("dbo.vw_Donations_ByYear",    "DonationYear")     },
        };

    public RunReport(ILogger<RunReport> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("RunReport")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "reports/run")] HttpRequest req)
    {
        _logger.LogInformation("RunReport triggered.");
// Deserialise filter from request body
        DonationFilter? filter;
        try
        {
            filter = await JsonSerializer.DeserializeAsync<DonationFilter>(
                req.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult(new { Error = "Request body is not valid JSON." });
        }

        if (filter is null)
            return new BadRequestObjectResult(new { Error = "Request body is required." });

        var organizationID =
            req.GetRequiredApiIdentity().EMAOrganizationID;

        if (filter.DateRange?.Start is not null &&
            filter.DateRange?.End is not null &&
            filter.DateRange.End.Value <
            filter.DateRange.Start.Value)
        {
            return new BadRequestObjectResult(
                new
                {
                    Error =
                        "The report end date must be on or after the start date."
                });
        }

        if (filter.DateRange?.Start is not null &&
            filter.DateRange?.End is not null &&
            (filter.DateRange.End.Value -
             filter.DateRange.Start.Value)
            .TotalDays > 366)
        {
            return new BadRequestObjectResult(
                new
                {
                    Error =
                        "The report date range cannot exceed 366 days."
                });
        }

        if (filter.Name?.Length > 200 ||
            filter.Methods?.Count > 100 ||
            filter.Funds?.Count > 100 ||
            filter.CurrencyTypes?.Count > 100 ||
            filter.Campuses?.Count > 100)
        {
            return new BadRequestObjectResult(
                new
                {
                    Error =
                        "One or more report filters exceed the supported limit."
                });
        }

        // Resolve Summarize → view name and date column from whitelist (never interpolate raw user input)
        string viewName   = "dbo.vw_Donations_Detail";
        string dateColumn = "DateTimeReceived";
        if (!string.IsNullOrWhiteSpace(filter.Summarize) &&
            SummarizeViewMap.TryGetValue(filter.Summarize, out var mapped))
        {
            viewName   = mapped.View;
            dateColumn = mapped.DateColumn;
        }

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            // Build WHERE clause — all values go through SqlParameter to prevent injection.
            // The date IS NOT NULL guard excludes the null-DateTimeReceived group that SQL Server
            // produces when donations have no received timestamp (GROUP BY treats NULLs as one group).
            var whereClauses = new List<string>
            {
                "EMAOrganizationID = @OrgID",
                $"{dateColumn} IS NOT NULL",
            };
            var parameters   = new List<SqlParameter>
            {
                new SqlParameter("@OrgID", organizationID)
            };

            // DateTimeReceived and DonationAmount only exist on the Detail view (Summarize=None).
            // Aggregated views roll those columns up, so date/amount filters only apply to Detail.
            bool isDetail = string.Equals(filter.Summarize, "None", StringComparison.OrdinalIgnoreCase)
                         || string.IsNullOrWhiteSpace(filter.Summarize);

            if (isDetail)
            {
                if (filter.DateRange?.Start is not null)
                {
                    whereClauses.Add("DateTimeReceived >= @DateStart");
                    parameters.Add(new SqlParameter("@DateStart", filter.DateRange.Start.Value));
                }

                if (filter.DateRange?.End is not null)
                {
                    whereClauses.Add("DateTimeReceived <= @DateEnd");
                    parameters.Add(new SqlParameter("@DateEnd", filter.DateRange.End.Value));
                }

                if (filter.Amount?.Min is not null)
                {
                    whereClauses.Add("DonationAmount >= @AmountMin");
                    parameters.Add(new SqlParameter("@AmountMin", filter.Amount.Min.Value));
                }

                if (filter.Amount?.Max is not null)
                {
                    whereClauses.Add("DonationAmount <= @AmountMax");
                    parameters.Add(new SqlParameter("@AmountMax", filter.Amount.Max.Value));
                }

                if (!string.IsNullOrWhiteSpace(filter.Name))
                {
                    whereClauses.Add("InteracDonorName LIKE '%' + @Name + '%'");
                    parameters.Add(new SqlParameter("@Name", filter.Name.Trim()));
                }

                if (filter.Methods is { Count: > 0 })
                {
                    var names = filter.Methods.Select((_, i) => $"@Method{i}").ToList();
                    whereClauses.Add($"EMAMethodOfGivingID IN ({string.Join(", ", names)})");
                    for (int i = 0; i < filter.Methods.Count; i++)
                        parameters.Add(new SqlParameter($"@Method{i}", filter.Methods[i]));
                }

                if (filter.Funds is { Count: > 0 })
                {
                    var names = filter.Funds.Select((_, i) => $"@Fund{i}").ToList();
                    whereClauses.Add($"EMAFundID IN ({string.Join(", ", names)})");
                    for (int i = 0; i < filter.Funds.Count; i++)
                        parameters.Add(new SqlParameter($"@Fund{i}", filter.Funds[i]));
                }

                if (filter.CurrencyTypes is { Count: > 0 })
                {
                    var names = filter.CurrencyTypes.Select((_, i) => $"@CurrencyType{i}").ToList();
                    whereClauses.Add($"EMACurrencyTypeID IN ({string.Join(", ", names)})");
                    for (int i = 0; i < filter.CurrencyTypes.Count; i++)
                        parameters.Add(new SqlParameter($"@CurrencyType{i}", filter.CurrencyTypes[i]));
                }

                // Campus filter deferred: EMACampusID is not a confirmed column on dbo.Donation.
                // When Roger confirms the campus-donation join, add the WHERE clause here.
            }

            // viewName and dateColumn are both from a whitelist — safe to interpolate directly
            string sql = $"""
                SELECT TOP (5000) *
                FROM   {viewName}
                WHERE  {string.Join(" AND ", whereClauses)}
                ORDER  BY {dateColumn} DESC;
                """;

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddRange(parameters.ToArray());

            using var reader = await cmd.ExecuteReaderAsync();

            // Read column names dynamically so any view's schema is handled automatically
            var columns = Enumerable.Range(0, reader.FieldCount)
                                    .Select(i => reader.GetName(i))
                                    .ToList();

            // Each row becomes a dictionary keyed by column name
            var rows = new List<Dictionary<string, object?>>();
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                    row[columns[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                rows.Add(row);
            }

            return new OkObjectResult(new { Columns = columns, Rows = rows });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running report for EMAOrganizationID {OrgID}", organizationID);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }
}
