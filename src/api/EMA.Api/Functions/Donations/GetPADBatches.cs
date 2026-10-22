using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Data.SqlClient;

namespace EMA.Api.Functions.Donations;

public class GetPADBatches
{
    private readonly string _connectionString;

    public GetPADBatches()
    {
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")
            ?? throw new InvalidOperationException("SqlConnectionString is not configured.");
    }

    public static string FormatDateRangeSummary(DateTime? minDate, DateTime? maxDate, int lineCount = 0)
    {
        if (minDate.HasValue && maxDate.HasValue)
        {
            return minDate.Value.Date == maxDate.Value.Date
                ? minDate.Value.ToString("yyyy'/'MM'/'dd", System.Globalization.CultureInfo.InvariantCulture)
                : $"{minDate.Value.ToString("yyyy'/'MM'/'dd", System.Globalization.CultureInfo.InvariantCulture)} To {maxDate.Value.ToString("yyyy'/'MM'/'dd", System.Globalization.CultureInfo.InvariantCulture)}";
        }
        return $"{lineCount} Lines";
    }

    [Function("GetPADBatches")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "donations/pad-batches")] HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");

        var queryParams = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        if (!int.TryParse(queryParams["EMAOrganizationID"], out int organizationId) || organizationId <= 0)
        {
            var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
            await badResponse.WriteStringAsync("Invalid or missing EMAOrganizationID.");
            return badResponse;
        }

        var list = new List<PADBatchDto>();

        using (var conn = new SqlConnection(_connectionString))
        {
            await conn.OpenAsync();

            string sql = @"
                SELECT 
                    b.PADBatchID,
                    b.PADBatchStatusID,
                    ISNULL(s.Description, 'PAD Details Imported') AS StatusDescription,
                    RTRIM(ISNULL((
                        SELECT TOP 1 btt.BankId 
                        FROM dbo.PADBatchLine l 
                        JOIN dbo.BankTransactionTypeKeyPhrase kp ON l.UnalteredText LIKE '%' + RTRIM(kp.KeyPhrase) + '%'
                        JOIN dbo.BankTransactionType btt ON kp.BankTransactionTypeID = btt.BankTransactionTypeID
                        WHERE l.PADBatchID = b.PADBatchID
                    ), '')) AS BankName,
                    b.DateTimeCreated,
                    ISNULL(u1.UserName, 'User #' + CAST(b.CreatedByUserID AS VARCHAR)) AS CreatedByUserName,
                    b.DateTimeUpdated,
                    ISNULL(u2.UserName, 'User #' + CAST(b.UpdatedByUserID AS VARCHAR)) AS UpdatedByUserName,
                    (SELECT MIN(l.PADDate) FROM dbo.PADBatchLine l WHERE l.PADBatchID = b.PADBatchID) AS MinDate,
                    (SELECT MAX(l.PADDate) FROM dbo.PADBatchLine l WHERE l.PADBatchID = b.PADBatchID) AS MaxDate,
                    (SELECT COUNT(*) FROM dbo.PADBatchLine l WHERE l.PADBatchID = b.PADBatchID) AS LineCount
                FROM dbo.PADBatch b
                LEFT JOIN dbo.PADBatchStatus s ON b.PADBatchStatusID = s.PadBatchStatusID
                LEFT JOIN dbo.[User] u1 ON b.CreatedByUserID = u1.UserID
                LEFT JOIN dbo.[User] u2 ON b.UpdatedByUserID = u2.UserID
                WHERE b.EMAOrganizationID = @OrgID
                ORDER BY b.PADBatchID DESC;";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@OrgID", organizationId);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var minDate = reader.IsDBNull(8) ? (DateTime?)null : reader.GetDateTime(8);
                var maxDate = reader.IsDBNull(9) ? (DateTime?)null : reader.GetDateTime(9);
                int lineCount = reader.IsDBNull(10) ? 0 : reader.GetInt32(10);

                string dateSummary = FormatDateRangeSummary(minDate, maxDate, lineCount);

                list.Add(new PADBatchDto
                {
                    PADBatchID = Convert.ToInt32(reader.GetValue(0)),
                    PADBatchStatusID = Convert.ToInt32(reader.GetValue(1)),
                    StatusDescription = reader.IsDBNull(2) ? "PAD Details Imported" : reader.GetString(2),
                    BankName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    DateTimeCreated = reader.GetDateTime(4),
                    CreatedByUserName = reader.IsDBNull(5) ? "User #" + reader.GetValue(5) : reader.GetString(5),
                    DateTimeUpdated = reader.GetDateTime(6),
                    UpdatedByUserName = reader.IsDBNull(7) ? "User #" + reader.GetValue(7) : reader.GetString(7),
                    TransactionDatesSummary = dateSummary
                });
            }
        }

        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        await response.WriteStringAsync(JsonSerializer.Serialize(list, jsonOptions));
        return response;
    }
}

public class PADBatchDto
{
    public int PADBatchID { get; set; }
    public int PADBatchStatusID { get; set; }
    public string StatusDescription { get; set; } = "";
    public string BankName { get; set; } = "";
    public DateTime DateTimeCreated { get; set; }
    public string CreatedByUserName { get; set; } = "";
    public DateTime DateTimeUpdated { get; set; }
    public string UpdatedByUserName { get; set; } = "";
    public string TransactionDatesSummary { get; set; } = "";
}
