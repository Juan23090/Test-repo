using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Text.Json;

namespace EMA_Api.Functions.Operations;

public class SaveSavingsCalculatorSettings
{
    private readonly ILogger<SaveSavingsCalculatorSettings> _logger;
    private readonly string _connectionString;

    public SaveSavingsCalculatorSettings(ILogger<SaveSavingsCalculatorSettings> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("SaveSavingsCalculatorSettings")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "savings-calculator/settings/{orgId:int}")] HttpRequest req,
        int orgId)
    {
        _logger.LogInformation("SaveSavingsCalculatorSettings triggered for org {OrgId}.", orgId);
        try
        {
            using var doc = await JsonDocument.ParseAsync(req.Body);
            var root = doc.RootElement;

            DateTime? startDate = null;
            if (TryGetProp(root, "StartDate", out var sd) && sd.ValueKind != JsonValueKind.Null)
                startDate = sd.GetDateTime();

            DateTime? endDate = null;
            if (TryGetProp(root, "EndDate", out var ed) && ed.ValueKind != JsonValueKind.Null)
                endDate = ed.GetDateTime();

            decimal? laborCostPerHour = null;
            if (TryGetProp(root, "LaborCostPerHour", out var lh) && lh.ValueKind != JsonValueKind.Null)
                laborCostPerHour = lh.GetDecimal();

            decimal? monthlySoftwareRental = null;
            if (TryGetProp(root, "MonthlySoftwareRental", out var ms) && ms.ValueKind != JsonValueKind.Null)
                monthlySoftwareRental = ms.GetDecimal();

            short? numberOfMonths = null;
            if (TryGetProp(root, "NumberOfMonths", out var nm) && nm.ValueKind != JsonValueKind.Null)
                numberOfMonths = nm.GetInt16();

            int? chmsId = null;
            if (TryGetProp(root, "ChMSID", out var ci) && ci.ValueKind != JsonValueKind.Null)
                chmsId = ci.GetInt32();

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(
                @"UPDATE Organization
                  SET SavingsCalculatorStartDate = @StartDate,
                      SavingsCalculatorEndDate = @EndDate,
                      SavingsCalculatorLaborCostPerHour = @LaborCostPerHour,
                      ChMSMonthlyRent = @MonthlySoftwareRental,
                      SavingsCalculatorNumberOfMonths = @NumberOfMonths,
                      ChMSID = @ChMSID,
                      DateTimeUpdated = GETUTCDATE(),
                      UpdatedByUserID = @UpdatedByUserID
                  WHERE EMAOrganizationID = @OrgID", conn);

            cmd.Parameters.AddWithValue("@OrgID", orgId);
            cmd.Parameters.AddWithValue("@StartDate", (object?)startDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@EndDate", (object?)endDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@LaborCostPerHour", (object?)laborCostPerHour ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@MonthlySoftwareRental", (object?)monthlySoftwareRental ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@NumberOfMonths", (object?)numberOfMonths ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ChMSID", (object?)chmsId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@UpdatedByUserID", req.GetRequiredApiIdentity().UserID);

            int rowsAffected = await cmd.ExecuteNonQueryAsync();
            if (rowsAffected == 0)
            {
                return new NotFoundObjectResult($"Organization with ID {orgId} not found.");
            }
            return new OkResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving savings calculator settings for org {OrgId}.", orgId);
            return new StatusCodeResult(500);
        }
    }

    private static bool TryGetProp(JsonElement element, string name, out JsonElement val)
    {
        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                val = prop.Value;
                return true;
            }
        }
        val = default;
        return false;
    }
}
