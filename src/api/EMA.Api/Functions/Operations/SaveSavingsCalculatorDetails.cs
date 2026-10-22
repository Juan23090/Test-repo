using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Text.Json;

namespace EMA_Api.Functions.Operations;

public class SaveSavingsCalculatorDetails
{
    private readonly ILogger<SaveSavingsCalculatorDetails> _logger;
    private readonly string _connectionString;

    public SaveSavingsCalculatorDetails(ILogger<SaveSavingsCalculatorDetails> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("SaveSavingsCalculatorDetails")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "savings-calculator/details/{orgId:int}")] HttpRequest req,
        int orgId)
    {
        _logger.LogInformation("SaveSavingsCalculatorDetails triggered for org {OrgId}.", orgId);
        try
        {
            using var doc = await JsonDocument.ParseAsync(req.Body);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Array)
            {
                return new BadRequestObjectResult("Expected a JSON array of detail items.");
            }

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var transaction = conn.BeginTransaction();

            try
            {
                foreach (var element in root.EnumerateArray())
                {
                    int methodId = 0;
                    if (TryGetProp(element, "EMAMethodOfGivingID", out var mi) && mi.ValueKind != JsonValueKind.Null)
                        methodId = mi.GetInt32();

                    string description = "";
                    if (TryGetProp(element, "Description", out var desc) && desc.ValueKind != JsonValueKind.Null)
                        description = desc.GetString() ?? "";

                    short sequence = 0;
                    if (TryGetProp(element, "Sequence", out var seq) && seq.ValueKind != JsonValueKind.Null)
                        sequence = seq.GetInt16();

                    int? annualNumberOfTransactions = null;
                    if (TryGetProp(element, "AnnualNumberOfTransactions", out var ant) && ant.ValueKind != JsonValueKind.Null)
                        annualNumberOfTransactions = ant.GetInt32();

                    decimal? annualValueOfTransactions = null;
                    if (TryGetProp(element, "AnnualValueOfTransactions", out var avt) && avt.ValueKind != JsonValueKind.Null)
                        annualValueOfTransactions = avt.GetDecimal();

                    decimal? laborMinutesPerTransaction = null;
                    if (TryGetProp(element, "LaborMinutesPerTransaction", out var lmt) && lmt.ValueKind != JsonValueKind.Null)
                        laborMinutesPerTransaction = lmt.GetDecimal();

                    string changeToEMAEtransfer = "N";
                    if (TryGetProp(element, "ChangeToEMAEtransfer", out var cet) && cet.ValueKind != JsonValueKind.Null)
                        changeToEMAEtransfer = cet.GetString() ?? "N";

                    string isActive = "Y";
                    if (TryGetProp(element, "IsActive", out var act) && act.ValueKind != JsonValueKind.Null)
                        isActive = act.GetString() ?? "Y";

                    if (methodId > 0)
                    {
                        string currentDescription = "";
                        using (var checkCmd = new SqlCommand(
                            @"SELECT Description 
                              FROM OrganizationMethodOfGiving 
                              WHERE EMAMethodOfGivingID = @MethodID 
                                AND EMAOrganizationID = @OrgID", conn, transaction))
                        {
                            checkCmd.Parameters.AddWithValue("@MethodID", methodId);
                            checkCmd.Parameters.AddWithValue("@OrgID", orgId);
                            var obj = await checkCmd.ExecuteScalarAsync();
                            if (obj != null)
                            {
                                currentDescription = obj.ToString()?.Trim() ?? "";
                            }
                        }

                        if (currentDescription.Equals("Credit & Debit", StringComparison.OrdinalIgnoreCase) ||
                            currentDescription.Equals("Canada Helps", StringComparison.OrdinalIgnoreCase))
                        {
                            description = currentDescription; 
                        }

                        using var cmd = new SqlCommand(
                            @"UPDATE OrganizationMethodOfGiving
                              SET Description = @Description,
                                  Sequence = @Sequence,
                                  AnnualNumberOfTransactions = @AnnualNumberOfTransactions,
                                  AnnualValueOfTransactions = @AnnualValueOfTransactions,
                                  LaborMinutesPerTransaction = @LaborMinutesPerTransaction,
                                  ChangeToEMAEtransfer = @ChangeToEMAEtransfer,
                                  IsActive = @IsActive,
                                  DateTimeUpdated = GETUTCDATE(),
                                  UpdatedbyUserID = @UpdatedByUserID
                              WHERE EMAMethodOfGivingID = @EMAMethodOfGivingID
                                AND EMAOrganizationID = @OrgID", conn, transaction);

                        cmd.Parameters.AddWithValue("@OrgID", orgId);
                        cmd.Parameters.AddWithValue("@EMAMethodOfGivingID", methodId);
                        cmd.Parameters.AddWithValue("@Description", description);
                        cmd.Parameters.AddWithValue("@Sequence", sequence);
                        cmd.Parameters.AddWithValue("@AnnualNumberOfTransactions", (object?)annualNumberOfTransactions ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@AnnualValueOfTransactions", (object?)annualValueOfTransactions ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@LaborMinutesPerTransaction", (object?)laborMinutesPerTransaction ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@ChangeToEMAEtransfer", changeToEMAEtransfer);
                        cmd.Parameters.AddWithValue("@IsActive", isActive);
                        cmd.Parameters.AddWithValue("@UpdatedByUserID", req.GetRequiredApiIdentity().UserID);

                        await cmd.ExecuteNonQueryAsync();
                    }
                    else
                    {
                        using var cmd = new SqlCommand(
                            @"INSERT INTO OrganizationMethodOfGiving (
                                  EMAOrganizationID, ChMSMethodOfGivingID, Sequence, Description,
                                  InvoiceGroup, CRAReceiptable, AcknowledgeToDonor, AcknowledgeToOrganization, AcknowledgeToEMASupport,
                                  IsActive, DateTimeCreated, CreatedByUserID,
                                  AnnualNumberOfTransactions, AnnualValueOfTransactions, LaborMinutesPerTransaction, ChangeToEMAEtransfer
                              ) VALUES (
                                  @OrgID, '0', @Sequence, @Description,
                                  0, 'Y', 'Y', 'Y', 'Y',
                                  'Y', GETUTCDATE(), @UpdatedByUserID,
                                  @AnnualNumberOfTransactions, @AnnualValueOfTransactions, @LaborMinutesPerTransaction, @ChangeToEMAEtransfer
                              )", conn, transaction);

                        cmd.Parameters.AddWithValue("@OrgID", orgId);
                        cmd.Parameters.AddWithValue("@Description", description);
                        cmd.Parameters.AddWithValue("@Sequence", sequence);
                        cmd.Parameters.AddWithValue("@AnnualNumberOfTransactions", (object?)annualNumberOfTransactions ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@AnnualValueOfTransactions", (object?)annualValueOfTransactions ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@LaborMinutesPerTransaction", (object?)laborMinutesPerTransaction ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@ChangeToEMAEtransfer", changeToEMAEtransfer);
                        cmd.Parameters.AddWithValue("@UpdatedByUserID", req.GetRequiredApiIdentity().UserID);

                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                await transaction.CommitAsync();
                return new OkResult();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Transaction failed while saving savings calculator details for org {OrgId}.", orgId);
                return new StatusCodeResult(500);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing body or establishing connection for org {OrgId}.", orgId);
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
