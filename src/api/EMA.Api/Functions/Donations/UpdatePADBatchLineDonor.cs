using System.Net;
using System.Text.Json;
using EMA.Api.Services.Security;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Data.SqlClient;

namespace EMA.Api.Functions.Donations;

public class UpdatePADBatchLineDonor
{
    private readonly string _connectionString;

    public UpdatePADBatchLineDonor()
    {
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")
            ?? throw new InvalidOperationException("SqlConnectionString is not configured.");
    }

    [Function("UpdatePADBatchLineDonor")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "donations/pad-update-line-donor")] HttpRequestData req)
    {
        var identity = req.GetRequiredApiIdentity();
        int organizationId = identity.EMAOrganizationID;

        if (organizationId <= 0)
        {
            var errResp = req.CreateResponse(HttpStatusCode.BadRequest);
            await errResp.WriteStringAsync("Invalid authenticated identity.");
            return errResp;
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");

        string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        using var doc = JsonDocument.Parse(requestBody);
        var root = doc.RootElement;

        int padBatchLineId = 0;
        int padBatchId = 0;
        string officialDonorName = "";

        foreach (var prop in root.EnumerateObject())
        {
            if (string.Equals(prop.Name, "PADBatchLineID", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(prop.Name, "padBatchLineID", StringComparison.OrdinalIgnoreCase))
            {
                padBatchLineId = prop.Value.GetInt32();
            }

            if (string.Equals(prop.Name, "PADBatchID", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(prop.Name, "padBatchID", StringComparison.OrdinalIgnoreCase))
            {
                padBatchId = prop.Value.GetInt32();
            }

            if (string.Equals(prop.Name, "OfficialDonorName", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(prop.Name, "officialDonorName", StringComparison.OrdinalIgnoreCase))
            {
                officialDonorName = prop.Value.GetString() ?? "";
            }
        }

        if (padBatchLineId <= 0 || padBatchId <= 0)
        {
            var errResp = req.CreateResponse(HttpStatusCode.BadRequest);
            await errResp.WriteStringAsync("Invalid PADBatchLineID or PADBatchID.");
            return errResp;
        }

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        string sql = @"
            UPDATE dbo.PADBatchLine 
            SET OfficialDonorName = @OfficialDonorName 
            WHERE EMAOrganizationID = @OrgID AND PADBatchID = @PADBatchID AND PADBatchLineID = @PADBatchLineID;";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@OfficialDonorName", (object?)officialDonorName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@OrgID", organizationId);
        cmd.Parameters.AddWithValue("@PADBatchID", padBatchId);
        cmd.Parameters.AddWithValue("@PADBatchLineID", padBatchLineId);

        int rows = await cmd.ExecuteNonQueryAsync();
        if (rows == 0)
        {
            var errResp = req.CreateResponse(HttpStatusCode.BadRequest);
            await errResp.WriteStringAsync("Batch line not found for this organization.");
            return errResp;
        }

        await response.WriteStringAsync(JsonSerializer.Serialize(new { success = true }));
        return response;
    }
}
