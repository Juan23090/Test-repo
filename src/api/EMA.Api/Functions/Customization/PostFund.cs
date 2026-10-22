using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Text.Json;

namespace EMA_Api.Functions.Customization;

public class PostFund
{
    private readonly ILogger<PostFund> _logger;
    private readonly string _connectionString;

    public PostFund(ILogger<PostFund> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString");
    }

    [Function("PostFund")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "customization/funds")] HttpRequest req)
    {
        _logger.LogInformation("PostFund triggered.");
        try
        {
            using var doc = await JsonDocument.ParseAsync(req.Body);
            var root = doc.RootElement;

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = new SqlCommand("EXEC sp_InsertFund @EMAOrganizationID, @FundName, @IsActive, @TaxDeductible, @CreatedByUserID", conn);
            cmd.Parameters.AddWithValue(
                "@EMAOrganizationID",
                req.GetRequiredApiIdentity().EMAOrganizationID);
            cmd.Parameters.AddWithValue("@FundName", root.TryGetProperty("FundName", out var name) ? name.GetString() ?? (object)DBNull.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@IsActive", "Y");
            cmd.Parameters.AddWithValue("@TaxDeductible", "N");
            cmd.Parameters.AddWithValue("@CreatedByUserID", req.GetRequiredApiIdentity().UserID);

            var newID = await cmd.ExecuteScalarAsync();
            return new OkObjectResult(new { EMAFundID = newID });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inserting fund.");
            return new StatusCodeResult(500);
        }
    }
}
