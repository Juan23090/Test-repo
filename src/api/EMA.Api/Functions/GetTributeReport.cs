using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace EMA_Api.Functions;

public class GetTributeReport
{
    private readonly ILogger<GetTributeReport> _logger;
    private readonly string _connectionString;

    public GetTributeReport(ILogger<GetTributeReport> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("GetTributeReport")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "reports/tributes/{orgID:int}")] HttpRequestData req,
        int orgID)
    {
        _logger.LogInformation("GetTributeReport triggered for org {OrgID}.", orgID);


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

        try
        {
            var rows = new List<TributeReportRow>();

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = new SqlCommand("sp_GetTributeReport", conn);
            cmd.CommandType = CommandType.StoredProcedure;

            cmd.Parameters.AddWithValue("@OrganizationID", orgID);
            cmd.Parameters.AddWithValue("@StartDate", startDate.ToDateTime(TimeOnly.MinValue));
            cmd.Parameters.AddWithValue("@EndDate", endDate.ToDateTime(TimeOnly.MinValue));

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var commentOrdinal = reader.GetOrdinal("Comment");
                var valueOrdinal = reader.GetOrdinal("TotalValue");

                rows.Add(new TributeReportRow
                {
                    EMATributeTypeID = reader.GetInt32(reader.GetOrdinal("EMATributeTypeID")),
                    Type = reader.IsDBNull(reader.GetOrdinal("Type"))
                        ? string.Empty
                        : reader.GetString(reader.GetOrdinal("Type")),
                    Comment = reader.IsDBNull(commentOrdinal)
                        ? string.Empty
                        : reader.GetString(commentOrdinal),
                    // Unnamed tributes are a real group (the largest in some orgs); the flag keeps
                    // them distinct from a name so they can drill down too.
                    HasName = !reader.IsDBNull(commentOrdinal),
                    DonationCount = reader.GetInt32(reader.GetOrdinal("DonationCount")),
                    TotalValue = reader.IsDBNull(valueOrdinal) ? 0m : reader.GetDecimal(valueOrdinal)
                });
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(rows);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching tribute report for org {OrgID}.", orgID);
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

    private static async Task<HttpResponseData> CreateBadRequest(HttpRequestData req, string message)
    {
        var response = req.CreateResponse(HttpStatusCode.BadRequest);
        await response.WriteAsJsonAsync(new { message });
        return response;
    }

    private sealed class TributeReportRow
    {
        public int EMATributeTypeID { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Comment { get; set; } = string.Empty;
        public bool HasName { get; set; }
        public int DonationCount { get; set; }
        public decimal TotalValue { get; set; }
    }
}
