using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace EMA.Api.Functions.Donations;

public class GetDonationWIP
{
    private readonly ILogger<GetDonationWIP> _logger;
    private readonly string _connectionString;

    public GetDonationWIP(ILogger<GetDonationWIP> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString");
    }

    [Function("GetDonationWIP")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "donations/wip/{id:long}")] HttpRequest req,
        long id)
    {
        _logger.LogInformation("GetDonationWIP triggered for donation {ID}.", id);
        try
        {
            var identity =
                req.GetRequiredApiIdentity();

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = new SqlCommand("dbo.sp_DonationWIPGet", conn);
            cmd.CommandType = System.Data.CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue(
                "@EMAOrganizationID",
                identity.EMAOrganizationID);
            cmd.Parameters.AddWithValue("@EMADonationID", id);
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var json = reader.IsDBNull(0) ? null : reader.GetString(0);
                if (json != null)
                    return new OkObjectResult(json);
            }
            return new NotFoundResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching donation WIP {ID}.", id);
            return new StatusCodeResult(500);
        }
    }
}
