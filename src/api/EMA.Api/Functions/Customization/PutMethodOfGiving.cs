using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Text.Json;
namespace EMA_Api.Functions.Customization;
public class PutMethodOfGiving
{
    private readonly ILogger<PutMethodOfGiving> _logger;
    private readonly string _connectionString;
    public PutMethodOfGiving(ILogger<PutMethodOfGiving> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString");
    }
    [Function("PutMethodOfGiving")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "customization/methods/{id:int}")] HttpRequest req,
        int id)
    {
        _logger.LogInformation("PutMethodOfGiving triggered for id {ID}.", id);
        try
        {
            using var doc = await JsonDocument.ParseAsync(req.Body);
            var root = doc.RootElement;
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = new SqlCommand("EXEC sp_UpdateMethodOfGiving @EMAOrganizationID, @EMAMethodOfGivingID, @Description, @IsActive, @CRAReceiptable, @JournalEntryInDetail, @AcknowledgeToDonor, @AcknowledgeToOrganization, @AcknowledgeToEMASupport, @AcknowledgementMessage, @UpdatedByUserID, @Sequence", conn);
            cmd.Parameters.AddWithValue(
                "@EMAOrganizationID",
                req.GetRequiredApiIdentity().EMAOrganizationID);
            cmd.Parameters.AddWithValue("@EMAMethodOfGivingID", id);
            cmd.Parameters.AddWithValue("@Description", root.TryGetProperty("Description", out var desc) ? desc.GetString() ?? (object)DBNull.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@IsActive", root.TryGetProperty("IsActive", out var ia) ? ia.GetString() : "Y");
            cmd.Parameters.AddWithValue("@CRAReceiptable", root.TryGetProperty("CRAReceiptable", out var cra) ? cra.GetString() : "Y");
            cmd.Parameters.AddWithValue("@JournalEntryInDetail", root.TryGetProperty("JournalEntryInDetail", out var jed) ? jed.GetString() : "N");
            cmd.Parameters.AddWithValue("@AcknowledgeToDonor", root.TryGetProperty("AcknowledgeToDonor", out var atd) ? atd.GetString() : "Y");
            cmd.Parameters.AddWithValue("@AcknowledgeToOrganization", root.TryGetProperty("AcknowledgeToOrganization", out var ato) ? ato.GetString() : "Y");
            cmd.Parameters.AddWithValue("@AcknowledgeToEMASupport", root.TryGetProperty("AcknowledgeToEMASupport", out var ate) ? ate.GetString() : "Y");
            cmd.Parameters.AddWithValue("@AcknowledgementMessage", root.TryGetProperty("AcknowledgementMessage", out var msg) && msg.ValueKind != JsonValueKind.Null ? msg.GetString() ?? (object)DBNull.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@UpdatedByUserID", req.GetRequiredApiIdentity().UserID);
            cmd.Parameters.AddWithValue("@Sequence", root.TryGetProperty("Sequence", out var seqProp) && seqProp.TryGetInt32(out var seqInt) ? seqInt : (object)DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
            return new OkResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating method of giving.");
            return new StatusCodeResult(500);
        }
    }
}
