using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Text.Json;
namespace EMA.Api.Functions.Donations;
public class PostEmailSave
{
    private readonly ILogger<PostEmailSave> _logger;
    private readonly string _connectionString;
    public PostEmailSave(ILogger<PostEmailSave> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString");
    }
    [Function("PostEmailSave")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "email/save/{id:long}")] HttpRequest req,
        long id)
    {
        _logger.LogInformation("PostEmailSave triggered for donation {ID}.", id);
        try
        {
            var identity =
                req.GetRequiredApiIdentity();

            using var doc = await JsonDocument.ParseAsync(req.Body);
            var root = doc.RootElement;
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = new SqlCommand(
                "EXEC dbo.sp_EmailSave @EMAOrganizationID = @EMAOrganizationID, @EMADonationID = @EMADonationID, @Comments = @Comments, @Tribute = @Tribute, @TributeType = @TributeType, @OfficialDonorName = @OfficialDonorName, @Address = @Address, @City = @City, @ProvinceStateCode = @ProvinceStateCode, @PostalCode = @PostalCode, @PhoneNumber = @PhoneNumber, @UpdatedByUserID = @UpdatedByUserID",
                conn);
            cmd.Parameters.AddWithValue(
                "@EMAOrganizationID",
                identity.EMAOrganizationID);
            cmd.Parameters.AddWithValue("@EMADonationID", id);
            cmd.Parameters.AddWithValue("@Comments", root.TryGetProperty("Comments", out var co) ? co.GetString() ?? (object)DBNull.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@Tribute", root.TryGetProperty("Tribute", out var tr) ? tr.GetString() ?? (object)DBNull.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@TributeType", root.TryGetProperty("TributeType", out var tt) ? tt.GetString() ?? (object)DBNull.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@OfficialDonorName", root.TryGetProperty("OfficialDonorName", out var on) ? on.GetString() ?? (object)DBNull.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@Address", root.TryGetProperty("Address", out var addr) ? addr.GetString() ?? (object)DBNull.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@City", root.TryGetProperty("City", out var city) ? city.GetString() ?? (object)DBNull.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@ProvinceStateCode", root.TryGetProperty("ProvinceStateCode", out var psc) ? psc.GetString() ?? (object)DBNull.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@PostalCode", root.TryGetProperty("PostalCode", out var pc) ? pc.GetString() ?? (object)DBNull.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@PhoneNumber", root.TryGetProperty("PhoneNumber", out var ph) ? ph.GetString() ?? (object)DBNull.Value : DBNull.Value);
            cmd.Parameters.AddWithValue(
                "@UpdatedByUserID",
                identity.UserID);
            await cmd.ExecuteNonQueryAsync();
            return new OkResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving email changes.");
            return new StatusCodeResult(500);
        }
    }
}
