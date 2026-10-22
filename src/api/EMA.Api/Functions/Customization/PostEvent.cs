using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Text.Json;

namespace EMA_Api.Functions.Customization;

public class PostEvent
{
    private readonly ILogger<PostEvent> _logger;
    private readonly string _connectionString;

    public PostEvent(ILogger<PostEvent> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("PostEvent")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "customization/events")] HttpRequest req)
    {
        _logger.LogInformation("PostEvent triggered.");
        try
        {
            using var doc = await JsonDocument.ParseAsync(req.Body);
            var root = doc.RootElement;

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = new SqlCommand("EXEC sp_InsertEvent @EMAOrganizationID, @Description, @IsActive, @CreatedByUserID", conn);
            cmd.Parameters.AddWithValue(
                "@EMAOrganizationID",
                req.GetRequiredApiIdentity().EMAOrganizationID);
            cmd.Parameters.AddWithValue("@Description", root.TryGetProperty("Description", out var desc) ? desc.GetString() ?? (object)DBNull.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@IsActive", "Y");
            cmd.Parameters.AddWithValue("@CreatedByUserID", req.GetRequiredApiIdentity().UserID);

            var newID = await cmd.ExecuteScalarAsync();
            return new OkObjectResult(new { EMAEventID = newID });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inserting event.");
            return new StatusCodeResult(500);
        }
    }
}
