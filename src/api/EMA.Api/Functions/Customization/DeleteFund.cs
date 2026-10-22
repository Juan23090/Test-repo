using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace EMA_Api.Functions.Customization;

public class DeleteFund
{
    private readonly ILogger<DeleteFund> _logger;
    private readonly string _connectionString;

    public DeleteFund(ILogger<DeleteFund> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString");
    }

    [Function("DeleteFund")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "customization/funds/{orgID:int}/{id:int}")] HttpRequest req,
        int orgID, int id)
    {
        _logger.LogInformation("DeleteFund triggered for id {ID}.", id);
        try
        {
            var organizationID =
                req.GetRequiredApiIdentity()
                    .EMAOrganizationID;

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = new SqlCommand("EXEC sp_DeleteFund @EMAFundID, @EMAOrganizationID", conn);
            cmd.Parameters.AddWithValue("@EMAFundID", id);
            cmd.Parameters.AddWithValue(
                "@EMAOrganizationID",
                organizationID);
            await cmd.ExecuteNonQueryAsync();
            return new OkResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting fund.");
            return new StatusCodeResult(500);
        }
    }
}
