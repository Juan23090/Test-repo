using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Text.Json;
namespace EMA_Api.Functions.Customization;
public class PutOrgAnonymousDonor
{
    private readonly ILogger<PutOrgAnonymousDonor> _logger;
    private readonly string _connectionString;
    public PutOrgAnonymousDonor(ILogger<PutOrgAnonymousDonor> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString");
    }
    [Function("PutOrgAnonymousDonor")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "customization/anonymous-donor/{orgID:int}")] HttpRequest req,
        int orgID)
    {
        _logger.LogInformation("PutOrgAnonymousDonor triggered for org {OrgID}.", orgID);
        try
        {
            using var doc = await JsonDocument.ParseAsync(req.Body);
            var root = doc.RootElement;
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = new SqlCommand("EXEC sp_UpdateOrgAnonymousDonor @EMAOrganizationID, @EMADonorIDForAnonymous, @UpdatedByUserID", conn);
            cmd.Parameters.AddWithValue("@EMAOrganizationID", orgID);
            cmd.Parameters.AddWithValue("@EMADonorIDForAnonymous", root.TryGetProperty("EMADonorIDForAnonymous", out var d) ? d.GetInt32() : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@UpdatedByUserID", req.GetRequiredApiIdentity().UserID);
            await cmd.ExecuteNonQueryAsync();
            return new OkResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating org anonymous donor.");
            return new StatusCodeResult(500);
        }
    }
}

