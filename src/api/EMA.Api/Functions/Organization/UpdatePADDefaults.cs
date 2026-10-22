using System.Data;
using System.Net;
using System.Text.Json;
using EMA.Api.Services.Security;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA_Api.Functions.Organization;

public class UpdatePADDefaults
{
    private readonly ILogger<UpdatePADDefaults> _logger;
    private readonly string _connectionString;

    public UpdatePADDefaults(ILogger<UpdatePADDefaults> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")
            ?? throw new InvalidOperationException("Missing SqlConnectionString.");
    }

    [Function("UpdatePADDefaults")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "organization/{orgID:int}/pad-defaults")] HttpRequestData req,
        int orgID)
    {
        _logger.LogInformation("UpdatePADDefaults triggered for organization {OrgID}.", orgID);

        try
        {
            var identity = req.GetRequiredApiIdentity();
            int organizationId = identity.EMAOrganizationID;

            if (organizationId <= 0 || organizationId != orgID)
            {
                var unauthResp = req.CreateResponse(HttpStatusCode.BadRequest);
                await unauthResp.WriteStringAsync("Organization mismatch or invalid identity.");
                return unauthResp;
            }

            var body = await req.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(body))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteStringAsync("Request body cannot be empty.");
                return badReq;
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            int? eventId = GetNullableInt(root, "EMAEventIDForPADs");
            int? sourceId = GetNullableInt(root, "EMASourceIDForPADs");
            int? methodId = GetNullableInt(root, "EMAMethodOfGivingIDForPADs");
            int? fundId = GetNullableInt(root, "EMAFundIDForPADs");
            int? campusId = GetNullableInt(root, "EMACampusIDForPADs");

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(req.FunctionContext.CancellationToken);

            if (eventId.HasValue && eventId.Value > 0)
            {
                const string checkSql = "SELECT COUNT(1) FROM dbo.OrganizationEvent WHERE EMAOrganizationID = @OrgID AND EMAEventID = @ID;";
                using var checkCmd = new SqlCommand(checkSql, connection);
                checkCmd.Parameters.AddWithValue("@OrgID", orgID);
                checkCmd.Parameters.AddWithValue("@ID", eventId.Value);
                if (Convert.ToInt32(await checkCmd.ExecuteScalarAsync(req.FunctionContext.CancellationToken)) == 0)
                {
                    var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badReq.WriteStringAsync("Referenced Event does not belong to this organization.");
                    return badReq;
                }
            }

            if (sourceId.HasValue && sourceId.Value > 0)
            {
                const string checkSql = "SELECT COUNT(1) FROM dbo.OrganizationDonationSource WHERE EMAOrganizationID = @OrgID AND EMADonationSourceID = @ID;";
                using var checkCmd = new SqlCommand(checkSql, connection);
                checkCmd.Parameters.AddWithValue("@OrgID", orgID);
                checkCmd.Parameters.AddWithValue("@ID", sourceId.Value);
                if (Convert.ToInt32(await checkCmd.ExecuteScalarAsync(req.FunctionContext.CancellationToken)) == 0)
                {
                    var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badReq.WriteStringAsync("Referenced Donation Source does not belong to this organization.");
                    return badReq;
                }
            }

            if (methodId.HasValue && methodId.Value > 0)
            {
                const string checkSql = "SELECT COUNT(1) FROM dbo.OrganizationMethodOfGiving WHERE EMAOrganizationID = @OrgID AND EMAMethodOfGivingID = @ID;";
                using var checkCmd = new SqlCommand(checkSql, connection);
                checkCmd.Parameters.AddWithValue("@OrgID", orgID);
                checkCmd.Parameters.AddWithValue("@ID", methodId.Value);
                if (Convert.ToInt32(await checkCmd.ExecuteScalarAsync(req.FunctionContext.CancellationToken)) == 0)
                {
                    var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badReq.WriteStringAsync("Referenced Method of Giving does not belong to this organization.");
                    return badReq;
                }
            }

            if (fundId.HasValue && fundId.Value > 0)
            {
                const string checkSql = "SELECT COUNT(1) FROM dbo.OrganizationFund WHERE EMAOrganizationID = @OrgID AND EMAFundID = @ID;";
                using var checkCmd = new SqlCommand(checkSql, connection);
                checkCmd.Parameters.AddWithValue("@OrgID", orgID);
                checkCmd.Parameters.AddWithValue("@ID", fundId.Value);
                if (Convert.ToInt32(await checkCmd.ExecuteScalarAsync(req.FunctionContext.CancellationToken)) == 0)
                {
                    var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badReq.WriteStringAsync("Referenced Fund does not belong to this organization.");
                    return badReq;
                }
            }

            if (campusId.HasValue && campusId.Value > 0)
            {
                const string checkSql = "SELECT COUNT(1) FROM dbo.OrganizationCampus WHERE EMAOrganizationID = @OrgID AND EMACampusID = @ID;";
                using var checkCmd = new SqlCommand(checkSql, connection);
                checkCmd.Parameters.AddWithValue("@OrgID", orgID);
                checkCmd.Parameters.AddWithValue("@ID", campusId.Value);
                if (Convert.ToInt32(await checkCmd.ExecuteScalarAsync(req.FunctionContext.CancellationToken)) == 0)
                {
                    var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badReq.WriteStringAsync("Referenced Campus does not belong to this organization.");
                    return badReq;
                }
            }

            const string sql = @"
                UPDATE dbo.Organization
                SET EMAEventIDForPADs = @EventID,
                    EMASourceIDForPADs = @SourceID,
                    EMAMethodOfGivingIDForPADs = @MethodID,
                    EMAFundIDForPADs = @FundID,
                    EMACampusIDForPADs = @CampusID,
                    DateTimeUpdated = SYSDATETIME()
                WHERE EMAOrganizationID = @OrgID;";

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@OrgID", orgID);
            command.Parameters.AddWithValue("@EventID", (object?)eventId ?? DBNull.Value);
            command.Parameters.AddWithValue("@SourceID", (object?)sourceId ?? DBNull.Value);
            command.Parameters.AddWithValue("@MethodID", (object?)methodId ?? DBNull.Value);
            command.Parameters.AddWithValue("@FundID", (object?)fundId ?? DBNull.Value);
            command.Parameters.AddWithValue("@CampusID", (object?)campusId ?? DBNull.Value);

            int rows = await command.ExecuteNonQueryAsync(req.FunctionContext.CancellationToken);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { Success = true, RowsAffected = rows });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating PAD defaults for organization {OrgID}.", orgID);
            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteStringAsync("Failed to update organization defaults.");
            return err;
        }
    }

    private static int? GetNullableInt(JsonElement root, string propName)
    {
        if (root.TryGetProperty(propName, out var elem) && elem.ValueKind == JsonValueKind.Number)
        {
            return elem.GetInt32();
        }
        return null;
    }
}
