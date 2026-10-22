using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
namespace EMA.Api.Functions.Donations;
public class PostEmailConfirm
{
    private readonly ILogger<PostEmailConfirm> _logger;
    private readonly string _connectionString;
    public PostEmailConfirm(ILogger<PostEmailConfirm> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString");
    }
    [Function("PostEmailConfirm")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "email/confirm/{id:long}")] HttpRequest req,
        long id)
    {
        _logger.LogInformation("PostEmailConfirm triggered for donation {ID}.", id);
        try
        {
            var identity =
                req.GetRequiredApiIdentity();

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = new SqlCommand(
                "EXEC dbo.sp_DonationConfirm @EMAOrganizationID = @EMAOrganizationID, @EMADonationID = @EMADonationID, @UpdatedByUserID = @UpdatedByUserID",
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
            _logger.LogError(ex, "Error confirming donation.");
            return new StatusCodeResult(500);
        }
    }
}
