using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Text.Json;

namespace EMA_Api.Functions.Operations;

public class MergeData
{
    private readonly ILogger<MergeData> _logger;
    private readonly string _connectionString;

    public MergeData(ILogger<MergeData> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("MergeData")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "operations/merge")] HttpRequest req)
    {
        _logger.LogInformation("MergeData triggered.");

        try
        {
            var body = await new StreamReader(req.Body).ReadToEndAsync();
            var request = JsonSerializer.Deserialize<MergeRequest>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (request == null)
            {
                return new BadRequestObjectResult(new { error = "Invalid JSON request body." });
            }

            if (string.IsNullOrWhiteSpace(request.EntityType))
            {
                return new BadRequestObjectResult(new { error = "EntityType is required." });
            }

            if (request.FromID <= 0 || request.ToID <= 0)
            {
                return new BadRequestObjectResult(new { error = "Valid FromID and ToID are required." });
            }

            if (request.FromID == request.ToID)
            {
                return new BadRequestObjectResult(new { error = "FromID and ToID cannot be the same." });
            }

            string storedProcedure;
            switch (request.EntityType.ToLower())
            {
                case "donors":
                    storedProcedure = "dbo.sp_MergeDonors";
                    break;
                case "funds":
                    storedProcedure = "dbo.sp_MergeFunds";
                    break;
                case "givingmethods":
                case "giving methods":
                    storedProcedure = "dbo.sp_MergeGivingMethods";
                    break;
                case "sources":
                case "source":
                    storedProcedure = "dbo.sp_MergeDonationSources";
                    break;
                default:
                    return new BadRequestObjectResult(new { error = $"Unsupported EntityType: '{request.EntityType}'." });
            }

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(storedProcedure, conn);
            cmd.CommandType = System.Data.CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@FromID", request.FromID);
            cmd.Parameters.AddWithValue("@ToID", request.ToID);
            cmd.Parameters.AddWithValue(
                "@EMAOrganizationID",
                req.GetRequiredApiIdentity()
                    .EMAOrganizationID);
            cmd.Parameters.AddWithValue(
                "@UpdatedByUserID",
                req.GetRequiredApiIdentity().UserID);

            await cmd.ExecuteNonQueryAsync();

            return new OkObjectResult(new { message = $"{request.EntityType} merged successfully." });
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "SQL error executing merge.");
            if (ex.Class >= 16)
            {
                return new BadRequestObjectResult(
                    new
                    {
                        error =
                            "The selected records could not be merged."
                    });
            }
            return new StatusCodeResult(500);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "System error executing merge.");
            return new StatusCodeResult(500);
        }
    }

    public class MergeRequest
    {
        public string EntityType { get; set; } = string.Empty;
        public int FromID { get; set; }
        public int ToID { get; set; }
        public int UpdatedByUserID { get; set; }
    }
}
