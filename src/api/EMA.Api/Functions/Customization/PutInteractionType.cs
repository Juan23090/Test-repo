using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Text.Json;

namespace EMA_Api.Functions.Customization;

public class PutInteractionType
{
    private readonly ILogger<PutInteractionType> _logger;
    private readonly string _connectionString;

    public PutInteractionType(ILogger<PutInteractionType> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("PutInteractionType")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "customization/interaction-types/{id:int}")] HttpRequest req,
        int id)
    {
        _logger.LogInformation("PutInteractionType triggered for id {ID}.", id);
        try
        {
            using var doc = await JsonDocument.ParseAsync(req.Body);
            var root = doc.RootElement;
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = new SqlCommand("EXEC sp_UpdateInteractionType @EMAOrganizationID, @EMAInteractionTypeID, @Description, @IsActive, @UpdatedByUserID, @Sequence", conn);
            cmd.Parameters.AddWithValue(
                "@EMAOrganizationID",
                req.GetRequiredApiIdentity().EMAOrganizationID);
            cmd.Parameters.AddWithValue("@EMAInteractionTypeID", id);
            cmd.Parameters.AddWithValue("@Description", root.TryGetProperty("Description", out var desc) ? desc.GetString() ?? (object)DBNull.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@IsActive", root.TryGetProperty("IsActive", out var ia) ? ia.GetString() : "Y");
            cmd.Parameters.AddWithValue("@UpdatedByUserID", req.GetRequiredApiIdentity().UserID);
            cmd.Parameters.AddWithValue("@Sequence", root.TryGetProperty("Sequence", out var seqProp) && seqProp.TryGetInt32(out var seqInt) ? seqInt : (object)DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
            return new OkResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating interaction type.");
            return new StatusCodeResult(500);
        }
    }
}
