using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace EMA_Api.Functions.Operations;

public class GetSavingsCalculatorData
{
    private readonly ILogger<GetSavingsCalculatorData> _logger;
    private readonly string _connectionString;

    public GetSavingsCalculatorData(ILogger<GetSavingsCalculatorData> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("GetSavingsCalculatorData")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "savings-calculator/{orgId:int}")] HttpRequest req,
        int orgId)
    {
        _logger.LogInformation("GetSavingsCalculatorData triggered for org {OrgId}.", orgId);
        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var orgSettings = await GetOrgSettingsAsync(conn, orgId);
            if (orgSettings == null)
            {
                return new NotFoundObjectResult($"Organization with ID {orgId} not found.");
            }

            var startDate = orgSettings.StartDate;
            var endDate = orgSettings.EndDate;

            if (startDate == null || endDate == null)
            {
                var prevYear = DateTime.UtcNow.Year - 1;
                startDate = new DateTime(prevYear, 1, 1);
                endDate = new DateTime(prevYear, 12, 31);
            }

            var numberOfMonths = orgSettings.NumberOfMonths;
            if (numberOfMonths == null || numberOfMonths == 0)
            {
                numberOfMonths = (short)(((endDate.Value.Year - startDate.Value.Year) * 12) + endDate.Value.Month - startDate.Value.Month + 1);
            }

            var donationCount = await GetDonationCountAsync(conn, orgId, startDate.Value, endDate.Value);
            bool insufficientHistory = donationCount < 100;
            string wording = insufficientHistory 
                ? "Enter Your Numbers in the Blue Cells to See Your Savings"
                : "Using the Donations Data Found";

            var methods = await GetMethodsOfGivingAsync(conn, orgId);

            var (canadaHelpsPercent, canadaHelpsPerTx) = await GetCanadaHelpsFeesAsync(conn);

            if (!insufficientHistory)
            {
                var aggregations = await GetDonationAggregationsAsync(conn, orgId, startDate.Value, endDate.Value);
                foreach (var method in methods)
                {
                    if (aggregations.TryGetValue(method.EMAMethodOfGivingID, out var agg))
                    {
                        method.AnnualNumberOfTransactions = agg.Count;
                        method.AnnualValueOfTransactions = agg.Sum;
                    }
                }
            }

            // Hardcoded check: inferring card and charity channels by name until a payment type column is added to the database.
            foreach (var method in methods)
            {
                var descLower = method.Description.ToLowerInvariant().Trim();
                if (descLower.Contains("credit") || descLower.Contains("debit") || descLower.Contains("card"))
                {
                    method.PercentBasedFee = orgSettings.ChMSPercentBasedFee ?? 0m;
                    method.PerTransactionFee = orgSettings.ChMSPerTransactionFee ?? 0m;
                    if (string.IsNullOrEmpty(method.ChangeToEMAEtransfer))
                    {
                        method.ChangeToEMAEtransfer = "Y";
                    }
                }
                else if (descLower.Contains("canadahelps") || descLower.Contains("canada helps"))
                {
                    method.PercentBasedFee = canadaHelpsPercent;
                    method.PerTransactionFee = canadaHelpsPerTx;
                    if (string.IsNullOrEmpty(method.ChangeToEMAEtransfer))
                    {
                        method.ChangeToEMAEtransfer = "Y";
                    }
                }
                else
                {
                    method.PercentBasedFee = 0m;
                    method.PerTransactionFee = 0m;
                    if (string.IsNullOrEmpty(method.ChangeToEMAEtransfer))
                    {
                        method.ChangeToEMAEtransfer = "N";
                    }
                }
            }

            decimal monthlySoftwareRental = orgSettings.ChMSMonthlyRent 
                ?? orgSettings.ChMSMonthlyRentDefault 
                ?? 0m;

            decimal? configuredEtransferRate = null;
            using (var rateCmd = new SqlCommand(
                @"SELECT TOP 1 EtransferChargePerTx 
                  FROM InvoiceRate br
                  JOIN Organization o ON o.InvoiceGroupID = br.InvoicingGroupID
                  WHERE o.EMAOrganizationID = @OrgID
                    AND br.RateEffectiveDateTime <= @EffectiveDate
                  ORDER BY br.RateEffectiveDateTime DESC", conn))
            {
                rateCmd.Parameters.AddWithValue("@OrgID", orgId);
                rateCmd.Parameters.AddWithValue("@EffectiveDate", endDate ?? DateTime.UtcNow);
                var rateVal = await rateCmd.ExecuteScalarAsync();
                if (rateVal != null && rateVal != DBNull.Value)
                {
                    configuredEtransferRate = Convert.ToDecimal(rateVal);
                }
            }

            var response = new
            {
                EMAOrganizationID = orgId,
                OrganizationName = orgSettings.OrganizationName,
                StartDate = startDate.Value,
                EndDate = endDate.Value,
                LaborCostPerHour = orgSettings.LaborCostPerHour ?? 30m,
                MonthlySoftwareRental = monthlySoftwareRental,
                NumberOfMonths = numberOfMonths,
                ChMSID = orgSettings.ChMSID,
                ChMSName = orgSettings.ChMSName,
                InsufficientHistory = insufficientHistory,
                Wording = wording,
                ConfiguredEtransferRate = configuredEtransferRate,
                Details = methods
            };

            return new OkObjectResult(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting savings calculator data for org {OrgId}.", orgId);
            return new StatusCodeResult(500);
        }
    }

    private async Task<OrgSettingsResult?> GetOrgSettingsAsync(SqlConnection conn, int orgId)
    {
        using var cmd = new SqlCommand(
            @"SELECT o.OrganizationName, 
                     o.SavingsCalculatorStartDate, o.SavingsCalculatorEndDate,
                     o.SavingsCalculatorLaborCostPerHour, o.ChMSMonthlyRent,
                     o.SavingsCalculatorNumberOfMonths, o.ChMSID, c.ChMSName,
                     c.ChMSMonthlyRentDefault, c.ChMSPercentBasedFee, c.ChMSPerTransactionFee
              FROM Organization o
              LEFT JOIN ChMS c ON o.ChMSID = c.ChMSID
              WHERE o.EMAOrganizationID = @OrgID", conn);
        cmd.Parameters.AddWithValue("@OrgID", orgId);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new OrgSettingsResult
            {
                OrganizationName = reader.GetString(0),
                StartDate = reader.IsDBNull(1) ? null : reader.GetDateTime(1),
                EndDate = reader.IsDBNull(2) ? null : reader.GetDateTime(2),
                LaborCostPerHour = reader.IsDBNull(3) ? null : reader.GetDecimal(3),
                ChMSMonthlyRent = reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                NumberOfMonths = reader.IsDBNull(5) ? null : reader.GetInt16(5),
                ChMSID = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                ChMSName = reader.IsDBNull(7) ? null : reader.GetString(7),
                ChMSMonthlyRentDefault = reader.IsDBNull(8) ? null : reader.GetDecimal(8),
                ChMSPercentBasedFee = reader.IsDBNull(9) ? null : reader.GetDecimal(9),
                ChMSPerTransactionFee = reader.IsDBNull(10) ? null : reader.GetDecimal(10)
            };
        }
        return null;
    }

    private async Task<int> GetDonationCountAsync(SqlConnection conn, int orgId, DateTime start, DateTime end)
    {
        using var cmd = new SqlCommand(
            @"SELECT COUNT(*) 
              FROM Donation 
              WHERE EMAOrganizationID = @OrgID 
                AND DateTimeReceived >= @Start 
                AND DateTimeReceived < @EndExclusive", conn);
        cmd.Parameters.AddWithValue("@OrgID", orgId);
        cmd.Parameters.AddWithValue("@Start", start);
        cmd.Parameters.AddWithValue("@EndExclusive", end.Date.AddDays(1));

        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private async Task<List<MethodOfGivingRow>> GetMethodsOfGivingAsync(SqlConnection conn, int orgId)
    {
        var list = new List<MethodOfGivingRow>();
        using var cmd = new SqlCommand(
            @"SELECT EMAMethodOfGivingID, Description, Sequence, 
                     AnnualNumberOfTransactions, AnnualValueOfTransactions, 
                     LaborMinutesPerTransaction, ChangeToEMAEtransfer, IsActive
              FROM OrganizationMethodOfGiving
              WHERE EMAOrganizationID = @OrgID
                AND IsActive = 'Y'
              ORDER BY Sequence", conn);
        cmd.Parameters.AddWithValue("@OrgID", orgId);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new MethodOfGivingRow
            {
                EMAMethodOfGivingID = reader.GetInt32(0),
                Description = reader.GetString(1),
                Sequence = reader.IsDBNull(2) ? (short)0 : reader.GetInt16(2),
                AnnualNumberOfTransactions = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                AnnualValueOfTransactions = reader.IsDBNull(4) ? 0m : reader.GetDecimal(4),
                LaborMinutesPerTransaction = reader.IsDBNull(5) ? 0m : reader.GetDecimal(5),
                ChangeToEMAEtransfer = reader.IsDBNull(6) ? "" : reader.GetString(6).Trim(),
                IsActive = reader.IsDBNull(7) ? "Y" : reader.GetString(7).Trim()
            });
        }
        return list;
    }

    private async Task<(decimal Percent, decimal PerTx)> GetCanadaHelpsFeesAsync(SqlConnection conn)
    {
        using var cmd = new SqlCommand(
            @"SELECT ChMSPercentBasedFee, ChMSPerTransactionFee 
              FROM ChMS 
              WHERE ChMSName = 'Canada Helps'", conn);
        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return (
                reader.IsDBNull(0) ? 0m : reader.GetDecimal(0),
                reader.IsDBNull(1) ? 0m : reader.GetDecimal(1)
            );
        }
        _logger.LogWarning("Canada Helps not found in ChMS table. Defaulting fees to 0.");
        return (0m, 0m);
    }

    private async Task<Dictionary<int, DonationAggregation>> GetDonationAggregationsAsync(SqlConnection conn, int orgId, DateTime start, DateTime end)
    {
        var dict = new Dictionary<int, DonationAggregation>();
        using var cmd = new SqlCommand(
            @"SELECT EMAMethodOfGivingID, COUNT(*), SUM(DonationAmount)
              FROM Donation
              WHERE EMAOrganizationID = @OrgID
                AND DateTimeReceived >= @Start
                AND DateTimeReceived < @EndExclusive
                AND EMAMethodOfGivingID IS NOT NULL
              GROUP BY EMAMethodOfGivingID", conn);
        cmd.Parameters.AddWithValue("@OrgID", orgId);
        cmd.Parameters.AddWithValue("@Start", start);
        cmd.Parameters.AddWithValue("@EndExclusive", end.Date.AddDays(1));

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var methodId = reader.GetInt32(0);
            dict[methodId] = new DonationAggregation
            {
                Count = reader.GetInt32(1),
                Sum = reader.IsDBNull(2) ? 0m : reader.GetDecimal(2)
            };
        }
        return dict;
    }

    private class OrgSettingsResult
    {
        public string OrganizationName { get; set; } = "";
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public decimal? LaborCostPerHour { get; set; }
        public decimal? ChMSMonthlyRent { get; set; }
        public short? NumberOfMonths { get; set; }
        public int? ChMSID { get; set; }
        public string? ChMSName { get; set; }
        public decimal? ChMSMonthlyRentDefault { get; set; }
        public decimal? ChMSPercentBasedFee { get; set; }
        public decimal? ChMSPerTransactionFee { get; set; }
    }

    private class MethodOfGivingRow
    {
        public int EMAMethodOfGivingID { get; set; }
        public string Description { get; set; } = "";
        public short Sequence { get; set; }
        public int AnnualNumberOfTransactions { get; set; }
        public decimal AnnualValueOfTransactions { get; set; }
        public decimal LaborMinutesPerTransaction { get; set; }
        public string ChangeToEMAEtransfer { get; set; } = "";
        public string IsActive { get; set; } = "";
        public decimal PercentBasedFee { get; set; }
        public decimal PerTransactionFee { get; set; }
    }

    private class DonationAggregation
    {
        public int Count { get; set; }
        public decimal Sum { get; set; }
    }
}
