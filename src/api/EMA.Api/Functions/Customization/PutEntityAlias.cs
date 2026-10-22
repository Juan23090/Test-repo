using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Text.Json;

namespace EMA_Api.Functions.Customization;

public class PutEntityAlias
{
    private readonly ILogger<PutEntityAlias> _logger;
    private readonly string _connectionString;

    public PutEntityAlias(ILogger<PutEntityAlias> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("PutEntityAlias")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "customization/entity-aliases/{orgID:int}")] HttpRequest req,
        int orgID)
    {
        _logger.LogInformation("PutEntityAlias triggered for org {OrgID}.", orgID);
        try
        {
            using var doc = await JsonDocument.ParseAsync(req.Body);
            var root = doc.RootElement;

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = new SqlCommand("EXEC sp_UpsertEntityAlias @EMAOrganizationID, @EntityName, @AliasName, @UpdatedByUserID", conn);
            cmd.Parameters.AddWithValue("@EMAOrganizationID", orgID);
            cmd.Parameters.AddWithValue("@EntityName", root.TryGetProperty("EntityName", out var en) ? en.GetString() ?? (object)DBNull.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@AliasName", root.TryGetProperty("AliasName", out var an) ? an.GetString() ?? (object)DBNull.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@UpdatedByUserID", req.GetRequiredApiIdentity().UserID);
            await cmd.ExecuteNonQueryAsync();
            return new OkResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error upserting entity alias.");
            return new StatusCodeResult(500);
        }
    }
}

