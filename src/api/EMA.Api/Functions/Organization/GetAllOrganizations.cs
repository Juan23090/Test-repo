using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Organization;

public sealed class GetAllOrganizations
{
    private readonly ILogger<GetAllOrganizations> _logger;
    private readonly string _connectionString;

    public GetAllOrganizations(ILogger<GetAllOrganizations> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")
            ?? throw new InvalidOperationException("Missing SqlConnectionString.");
    }

    [Function("GetAllOrganizations")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "organization/all")] HttpRequest request)
    {
        _logger.LogInformation("GetAllOrganizations triggered.");

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = """
                SELECT
                    EMAOrganizationID,
                    OrganizationName,
                    ContactName,
                    ContactPhone,
                    ContactEmail,
                    AdminName,
                    AdminPhone,
                    AdminEmail,
                    AnnualBudget,
                    OrganizationStatusID,
                    ChMSID,
                    AccountingSoftwareID
                FROM dbo.Organization;
                """;

            await using var command = new SqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync();

            var list = new List<Dictionary<string, object?>>();
            while (await reader.ReadAsync())
            {
                list.Add(new Dictionary<string, object?>
                {
                    ["emaOrganizationID"] = Convert.ToInt32(reader.GetValue(reader.GetOrdinal("EMAOrganizationID"))),
                    ["organizationName"] = reader.IsDBNull(reader.GetOrdinal("OrganizationName")) ? null : reader.GetString(reader.GetOrdinal("OrganizationName")),
                    ["contactName"] = reader.IsDBNull(reader.GetOrdinal("ContactName")) ? null : reader.GetString(reader.GetOrdinal("ContactName")),
                    ["contactPhone"] = reader.IsDBNull(reader.GetOrdinal("ContactPhone")) ? null : reader.GetString(reader.GetOrdinal("ContactPhone")),
                    ["contactEmail"] = reader.IsDBNull(reader.GetOrdinal("ContactEmail")) ? null : reader.GetString(reader.GetOrdinal("ContactEmail")),
                    ["adminName"] = reader.IsDBNull(reader.GetOrdinal("AdminName")) ? null : reader.GetString(reader.GetOrdinal("AdminName")),
                    ["adminPhone"] = reader.IsDBNull(reader.GetOrdinal("AdminPhone")) ? null : reader.GetString(reader.GetOrdinal("AdminPhone")),
                    ["adminEmail"] = reader.IsDBNull(reader.GetOrdinal("AdminEmail")) ? null : reader.GetString(reader.GetOrdinal("AdminEmail")),
                    ["annualBudget"] = reader.IsDBNull(reader.GetOrdinal("AnnualBudget")) ? null : reader.GetString(reader.GetOrdinal("AnnualBudget")),
                    ["organizationStatusID"] = reader.IsDBNull(reader.GetOrdinal("OrganizationStatusID")) ? null : Convert.ToInt32(reader.GetValue(reader.GetOrdinal("OrganizationStatusID"))),
                    ["chMSID"] = reader.IsDBNull(reader.GetOrdinal("ChMSID")) ? null : Convert.ToInt32(reader.GetValue(reader.GetOrdinal("ChMSID"))),
                    ["accountingSoftwareID"] = reader.IsDBNull(reader.GetOrdinal("AccountingSoftwareID")) ? null : Convert.ToInt32(reader.GetValue(reader.GetOrdinal("AccountingSoftwareID")))
                });
            }

            return new OkObjectResult(list);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching all organizations.");
            return new ObjectResult(new { message = "Unable to fetch organizations." })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }
}

