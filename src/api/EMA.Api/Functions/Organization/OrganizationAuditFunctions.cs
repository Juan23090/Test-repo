using System.ComponentModel.DataAnnotations.Schema;
using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Data;           // SqlDbType
using System.Text;           // StringBuilder
using System.Text.Json;
namespace EMA_Api.Functions.Organization;
public class OrganizationAuditFunctions
{
    private readonly string _connectionString;
    private readonly ILogger<OrganizationAuditFunctions> _logger;

    public OrganizationAuditFunctions(ILogger<OrganizationAuditFunctions> logger)
    {
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
        _logger = logger;
    }

    [Function("PostOrganizationAudit")]
    public async Task<HttpResponseData> PostOrganizationAudit(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post",
            Route = "organization/{orgID:int}/audits")] HttpRequestData req,
        int orgID)
    {
        try
        {

            using var bodyReader = new StreamReader(req.Body);
            var payloadJson = await bodyReader.ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(payloadJson))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { message = "Payload is required." });
                return bad;
            }

            try
            {
                using var _ = JsonDocument.Parse(payloadJson);
            }
            catch (JsonException)
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { message = "Invalid JSON payload." });
                return bad;
            }

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(
                @"INSERT INTO OrganizationAudit (EMAOrganizationID, OrganizationAuditPayload, DateTimeCreated)
                VALUES (@OrgID, @Payload, SYSUTCDATETIME());",
                conn);

            cmd.Parameters.Add("@OrgID", SqlDbType.Int).Value = orgID;
            cmd.Parameters.Add("@Payload", SqlDbType.NVarChar, -1).Value = payloadJson;
            _logger.LogInformation("PostOrganizationAudit triggered for org {OrgID}.", orgID);
            var rows = await cmd.ExecuteNonQueryAsync();
            if (rows == 0)
            {
                var failed = req.CreateResponse(HttpStatusCode.InternalServerError);
                await failed.WriteAsJsonAsync(new { message = "Failed to create audit." });
                return failed;
            }

            //Success
            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(new { message = "Audit created." });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating organization audit for org {OrgID}.", orgID);
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            return error;
        }
    }

    [Function("GetOrganizationAudits")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "organization/{orgID:int}/audits")] HttpRequestData req,
        int orgID)
    {
        _logger.LogInformation("GetOrganizationAudits triggered for org {OrgID}.", orgID);


        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(
                @"SELECT
                  JSON_QUERY(OrganizationAuditPayload) AS OrganizationAuditPayload,
                  DateTimeCreated
                  FROM OrganizationAudit
                  WHERE EMAOrganizationID = @OrgID
                  FOR JSON PATH;",
                conn);

            cmd.Parameters.AddWithValue("@OrgID", orgID);

            var sb = new StringBuilder();
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    sb.Append(reader.GetString(0));
                }
            }
            if (sb.Length == 0)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteAsJsonAsync(new { message = "Organization not found." });
                return notFoundResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await response.WriteStringAsync(sb.ToString());
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching organization audit for org {OrgID}.", orgID);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            return errorResponse;
        }
    }

    private static string? SafeColumnStringExtract(SqlDataReader reader, string column)
    {
        string value = string.Empty;
        value = reader.IsDBNull(reader.GetOrdinal(column))
                    ? string.Empty
                    : reader.GetString(reader.GetOrdinal(column));
        return value;
    }
    private static int SafeColumnIntExtract(SqlDataReader reader, string column)
    {
        int value = reader.IsDBNull(reader.GetOrdinal(column))
                    ? -1
                    : reader.GetInt32(reader.GetOrdinal(column));
        return value;
    }
}
