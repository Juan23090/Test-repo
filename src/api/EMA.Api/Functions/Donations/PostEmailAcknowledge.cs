using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
namespace EMA.Api.Functions.Donations;
public class PostEmailAcknowledge
{
    private readonly ILogger<PostEmailAcknowledge> _logger;
    private readonly string _connectionString;
    public PostEmailAcknowledge(ILogger<PostEmailAcknowledge> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString");
    }
    [Function("PostEmailAcknowledge")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "email/acknowledge/{id:long}")] HttpRequest req,
        long id)
    {
        _logger.LogInformation("PostEmailAcknowledge triggered for donation {ID}.", id);
        try
        {
            var identity =
                req.GetRequiredApiIdentity();

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = new SqlCommand(
                "EXEC dbo.sp_DonationAcknowledge @EMAOrganizationID = @EMAOrganizationID, @EMADonationID = @EMADonationID, @UpdatedByUserID = @UpdatedByUserID",
                conn);
            cmd.Parameters.AddWithValue(
                "@EMAOrganizationID",
                identity.EMAOrganizationID);
            cmd.Parameters.AddWithValue("@EMADonationID", id);
            cmd.Parameters.AddWithValue(
                "@UpdatedByUserID",
                identity.UserID);
            await cmd.ExecuteNonQueryAsync();
            return new OkResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error acknowledging donation.");
            return new StatusCodeResult(500);
        }
    }
}
