using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Text.Json;

namespace EMA_Api.Functions.Customization;

public class PostTributeType
{
    private readonly ILogger<PostTributeType> _logger;
    private readonly string _connectionString;

    public PostTributeType(ILogger<PostTributeType> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("PostTributeType")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "customization/tribute-types")] HttpRequest req)
    {
        _logger.LogInformation("PostTributeType triggered.");
        try
        {
            using var doc = await JsonDocument.ParseAsync(req.Body);
            var root = doc.RootElement;

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = new SqlCommand("EXEC sp_InsertTributeType @EMAOrganizationID, @Description, @IsActive, @CreatedByUserID", conn);
            cmd.Parameters.AddWithValue(
                "@EMAOrganizationID",
                req.GetRequiredApiIdentity().EMAOrganizationID);
            cmd.Parameters.AddWithValue("@Description", root.TryGetProperty("Description", out var desc) ? desc.GetString() ?? (object)DBNull.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@IsActive", "Y");
            cmd.Parameters.AddWithValue("@CreatedByUserID", req.GetRequiredApiIdentity().UserID);

            var newID = await cmd.ExecuteScalarAsync();
            return new OkObjectResult(new { EMATributeTypeID = newID });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inserting tribute type.");
            return new StatusCodeResult(500);
        }
    }
}
