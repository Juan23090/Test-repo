using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace EMA.Api.Functions.Donations;
public class EditDonation
{
    private readonly ILogger<EditDonation> _logger;
    private readonly string _connectionString;

    public EditDonation(ILogger<EditDonation> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString");
    }

    [Function("EditDonation")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "donations/edit/{id:long}")] HttpRequest req,
        long id)
    {
        _logger.LogInformation("EditDonation triggered for donation {ID}.", id);
        try
        {
            var identity =
                req.GetRequiredApiIdentity();

            var body = await new StreamReader(req.Body).ReadToEndAsync();
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = new SqlCommand("dbo.sp_DonationEditBegin", conn);
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
            _logger.LogError(ex, "Error beginning donation edit.");
            return new StatusCodeResult(500);
        }
    }
}
