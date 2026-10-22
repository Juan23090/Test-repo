using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Text.Json;
namespace EMA_Api.Functions.Customization;
public class PutCampus
{
    private readonly ILogger<PutCampus> _logger;
    private readonly string _connectionString;
    public PutCampus(ILogger<PutCampus> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString");
    }
    [Function("PutCampus")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "customization/campuses/{id:int}")] HttpRequest req,
        int id)
    {
        _logger.LogInformation("PutCampus triggered for id {ID}.", id);
        try
        {
            var identity =
                req.GetRequiredApiIdentity();

            using var doc = await JsonDocument.ParseAsync(req.Body);
            var root = doc.RootElement;
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = new SqlCommand(
                "EXEC dbo.sp_UpdateCampus @EMAOrganizationID = @EMAOrganizationID, @EMACampusID = @EMACampusID, @Name = @Name, @IsActive = @IsActive, @UpdatedByUserID = @UpdatedByUserID, @Sequence = @Sequence, @DefaultCampusFundID = @DefaultCampusFundID",
                conn);
            cmd.Parameters.AddWithValue(
                "@EMAOrganizationID",
                identity.EMAOrganizationID);
            cmd.Parameters.AddWithValue("@EMACampusID", id);
            cmd.Parameters.AddWithValue("@Name", root.TryGetProperty("Name", out var name) ? name.GetString() ?? (object)DBNull.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@IsActive", root.TryGetProperty("IsActive", out var ia) ? ia.GetString() : "Y");
            cmd.Parameters.AddWithValue(
                "@UpdatedByUserID",
                identity.UserID);
            cmd.Parameters.AddWithValue("@Sequence", root.TryGetProperty("Sequence", out var seqProp) && seqProp.TryGetInt32(out var seqInt) ? seqInt : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@DefaultCampusFundID", root.TryGetProperty("DefaultCampusFundID", out var dcf) && dcf.TryGetInt32(out var dcfInt) ? dcfInt : (object)DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
            return new OkResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating campus.");
            return new StatusCodeResult(500);
        }
    }
}
