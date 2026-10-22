using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Text.Json;

namespace EMA_Api.Functions.Customization;

public class PostDonorStatus
{
    private readonly ILogger<PostDonorStatus> _logger;
    private readonly string _connectionString;

    public PostDonorStatus(ILogger<PostDonorStatus> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("PostDonorStatus")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "customization/donor-statuses")] HttpRequest req)
    {
        _logger.LogInformation("PostDonorStatus triggered.");
        try
        {
            using var doc = await JsonDocument.ParseAsync(req.Body);
            var root = doc.RootElement;

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = new SqlCommand("EXEC sp_InsertDonorStatus @EMAOrganizationID, @Description, @IsActive", conn);
            cmd.Parameters.AddWithValue(
                "@EMAOrganizationID",
                req.GetRequiredApiIdentity().EMAOrganizationID);
            cmd.Parameters.AddWithValue("@Description", root.TryGetProperty("Description", out var desc) ? desc.GetString() ?? (object)DBNull.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@IsActive", true);

            var newID = await cmd.ExecuteScalarAsync();
            return new OkObjectResult(new { DonorStatusID = newID });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inserting donor status.");
            return new StatusCodeResult(500);
        }
    }
}
