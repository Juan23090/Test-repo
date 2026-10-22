using System.Text.Json;
using EMA.Api.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Banks;

public class SaveBankTransactionType
{
    private readonly ILogger<SaveBankTransactionType> _logger;
    private readonly string _connectionString;

    public SaveBankTransactionType(ILogger<SaveBankTransactionType> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("PostBankTransactionType")]
    public async Task<IActionResult> Post(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "banks/transaction-types")] HttpRequest req)
    {
        _logger.LogInformation("PostBankTransactionType triggered.");

        CreateBankTransactionTypeRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<CreateBankTransactionTypeRequest>(
                req.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult(new { message = "Invalid request body." });
        }

        if (request is null)
        {
            return new BadRequestObjectResult(new { message = "Request body is required." });
        }

        var bankId = BankMappingHelper.TrimOrEmpty(request.BankId);
        var transactionTypeId = BankMappingHelper.TrimOrEmpty(request.TransactionTypeID);
        var instructionsUrl = string.IsNullOrWhiteSpace(request.InstructionsURL) ? null : request.InstructionsURL.Trim();

        if (string.IsNullOrWhiteSpace(bankId))
        {
            return new BadRequestObjectResult(new { message = "Bank ID is required." });
        }

        if (string.IsNullOrWhiteSpace(transactionTypeId))
        {
            return new BadRequestObjectResult(new { message = "Transaction Type ID is required." });
        }

        if (!BankMappingHelper.IsValidInstructionsUrl(instructionsUrl))
        {
            return new BadRequestObjectResult(new { message = "Instructions URL must be a valid HTTPS URL (256 characters or fewer)." });
        }

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var checkCmd = new SqlCommand(
                "SELECT COUNT(1) FROM dbo.BankTransactionType WHERE BankId = @BankId AND TransactionTypeID = @TransactionTypeID",
                conn);
            checkCmd.Parameters.AddWithValue("@BankId", bankId);
            checkCmd.Parameters.AddWithValue("@TransactionTypeID", transactionTypeId);
            var exists = (int?)await checkCmd.ExecuteScalarAsync() > 0;

            if (exists)
            {
                return new ConflictObjectResult(new { message = "This bank already has this transaction type configured." });
            }

            using var insertCmd = new SqlCommand(@"
                INSERT INTO dbo.BankTransactionType (BankId, TransactionTypeID, InstructionsURL, IsActive)
                VALUES (@BankId, @TransactionTypeID, @InstructionsURL, @IsActive);
                SELECT SCOPE_IDENTITY();",
                conn);
            insertCmd.Parameters.AddWithValue("@BankId", bankId);
            insertCmd.Parameters.AddWithValue("@TransactionTypeID", transactionTypeId);
            insertCmd.Parameters.AddWithValue("@InstructionsURL", (object?)instructionsUrl ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("@IsActive", BankMappingHelper.ToActiveChar(request.IsActive));

            var scalar = await insertCmd.ExecuteScalarAsync();
            var newId = Convert.ToInt32(scalar);

            return new OkObjectResult(new
            {
                BankTransactionTypeID = newId,
                BankId = bankId,
                TransactionTypeID = transactionTypeId,
                InstructionsURL = instructionsUrl,
                IsActive = request.IsActive
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating bank transaction type.");
            return new StatusCodeResult(500);
        }
    }

    [Function("PutBankTransactionType")]
    public async Task<IActionResult> Put(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "banks/transaction-types/{id:int}")] HttpRequest req,
        int id)
    {
        _logger.LogInformation("PutBankTransactionType triggered for {BankTransactionTypeID}.", id);

        if (id <= 0)
        {
            return new BadRequestObjectResult(new { message = "Valid Bank Transaction Type ID is required." });
        }

        UpdateBankTransactionTypeRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<UpdateBankTransactionTypeRequest>(
                req.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult(new { message = "Invalid request body." });
        }

        if (request is null)
        {
            return new BadRequestObjectResult(new { message = "Request body is required." });
        }

        var instructionsUrl = string.IsNullOrWhiteSpace(request.InstructionsURL) ? null : request.InstructionsURL.Trim();
        if (!BankMappingHelper.IsValidInstructionsUrl(instructionsUrl))
        {
            return new BadRequestObjectResult(new { message = "Instructions URL must be a valid HTTPS URL (256 characters or fewer)." });
        }

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var updateCmd = new SqlCommand(@"
                UPDATE dbo.BankTransactionType 
                SET InstructionsURL = @InstructionsURL, IsActive = @IsActive 
                WHERE BankTransactionTypeID = @BankTransactionTypeID",
                conn);
            updateCmd.Parameters.AddWithValue("@BankTransactionTypeID", id);
            updateCmd.Parameters.AddWithValue("@InstructionsURL", (object?)instructionsUrl ?? DBNull.Value);
            updateCmd.Parameters.AddWithValue("@IsActive", BankMappingHelper.ToActiveChar(request.IsActive));

            var rowsAffected = await updateCmd.ExecuteNonQueryAsync();
            if (rowsAffected == 0)
            {
                return new NotFoundObjectResult(new { message = "Bank transaction type not found." });
            }

            return new OkObjectResult(new
            {
                BankTransactionTypeID = id,
                InstructionsURL = instructionsUrl,
                IsActive = request.IsActive
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating bank transaction type {BankTransactionTypeID}.", id);
            return new StatusCodeResult(500);
        }
    }
}
