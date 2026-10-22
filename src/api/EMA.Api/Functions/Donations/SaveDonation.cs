using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace EMA.Api.Functions.Donations;
public class SaveDonation
{
    private readonly ILogger<SaveDonation> _logger;
    private readonly string _connectionString;

    public SaveDonation(ILogger<SaveDonation> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString");
    }

    [Function("SaveDonation")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "donations/save/{id:long}")] HttpRequest req,
        long id)
    {
        _logger.LogInformation("SaveDonation triggered for donation {ID}.", id);
        try
        {
            var identity =
                req.GetRequiredApiIdentity();

            var body = await new StreamReader(req.Body).ReadToEndAsync();
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = new SqlCommand("dbo.sp_DonationSave", conn);
            cmd.CommandType = System.Data.CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue(
                "@EMAOrganizationID",
                identity.EMAOrganizationID);
            cmd.Parameters.AddWithValue("@EMADonationID", id);
            cmd.Parameters.AddWithValue("@JsonPayload", body);
            cmd.Parameters.AddWithValue(
                "@UpdatedByUserID",
                identity.UserID);
            await cmd.ExecuteNonQueryAsync();
            return new OkResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving donation.");
            return new StatusCodeResult(500);
        }
    }
}
