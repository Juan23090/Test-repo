using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace EMA_Api.Functions.Customization;

public class DeleteCampus
{
    private readonly ILogger<DeleteCampus> _logger;
    private readonly string _connectionString;

    public DeleteCampus(ILogger<DeleteCampus> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString");
    }

    [Function("DeleteCampus")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "customization/campuses/{orgID:int}/{id:int}")] HttpRequest req,
        int orgID, int id)
    {
        _logger.LogInformation("DeleteCampus triggered for id {ID}.", id);
        try
        {
            var organizationID =
                req.GetRequiredApiIdentity()
                    .EMAOrganizationID;

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = new SqlCommand("EXEC sp_DeleteCampus @EMACampusID, @EMAOrganizationID", conn);
            cmd.Parameters.AddWithValue("@EMACampusID", id);
            cmd.Parameters.AddWithValue(
                "@EMAOrganizationID",
                organizationID);
            await cmd.ExecuteNonQueryAsync();
            return new OkResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting campus.");
            return new StatusCodeResult(500);
        }
    }
}
