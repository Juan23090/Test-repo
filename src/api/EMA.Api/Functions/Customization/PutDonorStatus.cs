using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Text.Json;

namespace EMA_Api.Functions.Customization;

public class PutDonorStatus
{
    private readonly ILogger<PutDonorStatus> _logger;
    private readonly string _connectionString;

    public PutDonorStatus(ILogger<PutDonorStatus> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("PutDonorStatus")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "customization/donor-statuses/{id:int}")] HttpRequest req,
        int id)
    {
        _logger.LogInformation("PutDonorStatus triggered for id {ID}.", id);
        try
        {
            using var doc = await JsonDocument.ParseAsync(req.Body);
            var root = doc.RootElement;
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = new SqlCommand("EXEC sp_UpdateDonorStatus @EMAOrganizationID, @DonorStatusID, @Description, @IsActive, @Sequence", conn);
            cmd.Parameters.AddWithValue(
                "@EMAOrganizationID",
                req.GetRequiredApiIdentity().EMAOrganizationID);
            cmd.Parameters.AddWithValue("@DonorStatusID", id);
            cmd.Parameters.AddWithValue("@Description", root.TryGetProperty("Description", out var desc) ? desc.GetString() ?? (object)DBNull.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@IsActive", root.TryGetProperty("IsActive", out var ia) && ia.ValueKind == JsonValueKind.True);
            cmd.Parameters.AddWithValue("@Sequence", root.TryGetProperty("Sequence", out var seq) && seq.TryGetInt32(out var seqVal) ? seqVal : (object)DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
            return new OkResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating donor status.");
            return new StatusCodeResult(500);
        }
    }
}
