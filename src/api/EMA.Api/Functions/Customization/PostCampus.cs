using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Text.Json;

namespace EMA_Api.Functions.Customization;

public class PostCampus
{
    private readonly ILogger<PostCampus> _logger;
    private readonly string _connectionString;

    public PostCampus(ILogger<PostCampus> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString");
    }

    [Function("PostCampus")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "customization/campuses")] HttpRequest req)
    {
        _logger.LogInformation("PostCampus triggered.");
        try
        {
            using var doc = await JsonDocument.ParseAsync(req.Body);
            var root = doc.RootElement;

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = new SqlCommand("EXEC sp_InsertCampus @EMAOrganizationID, @CampusName, @IsActive, @CreatedByUserID", conn);
            cmd.Parameters.AddWithValue(
                "@EMAOrganizationID",
                req.GetRequiredApiIdentity().EMAOrganizationID);
            cmd.Parameters.AddWithValue("@CampusName", root.TryGetProperty("CampusName", out var name) ? name.GetString() ?? (object)DBNull.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@IsActive", "Y");
            cmd.Parameters.AddWithValue("@CreatedByUserID", req.GetRequiredApiIdentity().UserID);

            var newID = await cmd.ExecuteScalarAsync();
            return new OkObjectResult(new { EMACampusID = newID });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inserting campus.");
            return new StatusCodeResult(500);
        }
    }
}
