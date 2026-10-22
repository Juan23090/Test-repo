using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Text.Json;
namespace EMA_Api.Functions.Customization;
public class PutFund
{
    private readonly ILogger<PutFund> _logger;
    private readonly string _connectionString;
    public PutFund(ILogger<PutFund> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString");
    }
    [Function("PutFund")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "customization/funds/{id:int}")] HttpRequest req,
        int id)
    {
        _logger.LogInformation("PutFund triggered for id {ID}.", id);
        try
        {
            var identity =
                req.GetRequiredApiIdentity();

            using var doc = await JsonDocument.ParseAsync(req.Body);
            var root = doc.RootElement;
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = new SqlCommand(
                "EXEC dbo.sp_UpdateFund @EMAOrganizationID = @EMAOrganizationID, @EMAFundID = @EMAFundID, @Name = @Name, @IsActive = @IsActive, @TaxDeductible = @TaxDeductible, @EMACampusID = @EMACampusID, @UpdatedByUserID = @UpdatedByUserID, @Sequence = @Sequence",
                conn);
            cmd.Parameters.AddWithValue(
                "@EMAOrganizationID",
                identity.EMAOrganizationID);
            cmd.Parameters.AddWithValue("@EMAFundID", id);
            cmd.Parameters.AddWithValue("@Name", root.TryGetProperty("Name", out var name) ? name.GetString() ?? (object)DBNull.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@IsActive", root.TryGetProperty("IsActive", out var ia) ? ia.GetString() : "Y");
            cmd.Parameters.AddWithValue("@TaxDeductible", root.TryGetProperty("TaxDeductible", out var td) ? td.GetString() : "N");
            cmd.Parameters.AddWithValue("@EMACampusID", root.TryGetProperty("EMACampusID", out var ec) && ec.ValueKind != JsonValueKind.Null ? ec.GetInt32() : (object)DBNull.Value);
            cmd.Parameters.AddWithValue(
                "@UpdatedByUserID",
                identity.UserID);
            cmd.Parameters.AddWithValue("@Sequence", root.TryGetProperty("Sequence", out var seqProp) && seqProp.TryGetInt32(out var seqInt) ? seqInt : (object)DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
            return new OkResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating fund.");
            return new StatusCodeResult(500);
        }
    }
}
