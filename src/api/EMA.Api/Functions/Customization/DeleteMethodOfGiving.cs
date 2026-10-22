using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace EMA_Api.Functions.Customization;

public class DeleteMethodOfGiving
{
    private readonly ILogger<DeleteMethodOfGiving> _logger;
    private readonly string _connectionString;

    public DeleteMethodOfGiving(ILogger<DeleteMethodOfGiving> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString");
    }

    [Function("DeleteMethodOfGiving")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "customization/methods/{orgID:int}/{id:int}")] HttpRequest req,
        int orgID, int id)
    {
        _logger.LogInformation("DeleteMethodOfGiving triggered for id {ID}.", id);
        try
        {
            var organizationID =
                req.GetRequiredApiIdentity()
                    .EMAOrganizationID;

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = new SqlCommand("EXEC sp_DeleteMethodOfGiving @EMAMethodOfGivingID, @EMAOrganizationID", conn);
            cmd.Parameters.AddWithValue("@EMAMethodOfGivingID", id);
            cmd.Parameters.AddWithValue(
                "@EMAOrganizationID",
                organizationID);
            await cmd.ExecuteNonQueryAsync();
            return new OkResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting method of giving.");
            return new StatusCodeResult(500);
        }
    }
}
