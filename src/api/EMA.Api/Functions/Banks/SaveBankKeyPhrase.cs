using System.Text.Json;
using EMA.Api.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Banks;

public class SaveBankKeyPhrase
{
    private readonly ILogger<SaveBankKeyPhrase> _logger;
    private readonly string _connectionString;

    public SaveBankKeyPhrase(ILogger<SaveBankKeyPhrase> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("PostBankKeyPhrase")]
    public async Task<IActionResult> Post(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "banks/key-phrases")] HttpRequest req)
    {
        _logger.LogInformation("PostBankKeyPhrase triggered.");

        CreateBankKeyPhraseRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<CreateBankKeyPhraseRequest>(
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

        if (request.BankTransactionTypeID <= 0)
        {
            return new BadRequestObjectResult(new { message = "Valid Bank Transaction Type ID is required." });
        }

        var keyPhrase = BankMappingHelper.TrimOrEmpty(request.KeyPhrase);
        if (string.IsNullOrWhiteSpace(keyPhrase) || keyPhrase.Length > 100)
        {
            return new BadRequestObjectResult(new { message = "Key Phrase is required and must be 100 characters or fewer." });
        }

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            // The database column in dbo.BankTransactionTypeKeyPhrase is named BankTransactionTypeKeyPhrase without ID
            using var insertCmd = new SqlCommand(@"
                INSERT INTO dbo.BankTransactionTypeKeyPhrase (BankTransactionTypeID, KeyPhrase, IsActive)
                VALUES (@BankTransactionTypeID, @KeyPhrase, @IsActive);
                SELECT SCOPE_IDENTITY();",
                conn);
            insertCmd.Parameters.AddWithValue("@BankTransactionTypeID", request.BankTransactionTypeID);
            insertCmd.Parameters.AddWithValue("@KeyPhrase", keyPhrase);
            insertCmd.Parameters.AddWithValue("@IsActive", BankMappingHelper.ToActiveChar(request.IsActive));

            var scalar = await insertCmd.ExecuteScalarAsync();
            var newId = Convert.ToInt32(scalar);

            return new OkObjectResult(new
            {
                BankTransactionTypeKeyPhraseID = newId,
                BankTransactionTypeID = request.BankTransactionTypeID,
                KeyPhrase = keyPhrase,
                IsActive = request.IsActive
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating bank key phrase.");
            return new StatusCodeResult(500);
        }
    }

    [Function("PutBankKeyPhrase")]
    public async Task<IActionResult> Put(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "banks/key-phrases/{id:int}")] HttpRequest req,
        int id)
    {
        _logger.LogInformation("PutBankKeyPhrase triggered for {BankTransactionTypeKeyPhrase}.", id);

        if (id <= 0)
        {
            return new BadRequestObjectResult(new { message = "Valid Bank Key Phrase ID is required." });
        }

        UpdateBankKeyPhraseRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<UpdateBankKeyPhraseRequest>(
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

        var keyPhrase = BankMappingHelper.TrimOrEmpty(request.KeyPhrase);
        if (string.IsNullOrWhiteSpace(keyPhrase) || keyPhrase.Length > 100)
        {
            return new BadRequestObjectResult(new { message = "Key Phrase is required and must be 100 characters or fewer." });
        }

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            // The database column in dbo.BankTransactionTypeKeyPhrase is named BankTransactionTypeKeyPhrase without ID
            using var updateCmd = new SqlCommand(@"
                UPDATE dbo.BankTransactionTypeKeyPhrase 
                SET KeyPhrase = @KeyPhrase, IsActive = @IsActive 
                WHERE BankTransactionTypeKeyPhrase = @BankTransactionTypeKeyPhrase",
                conn);
            updateCmd.Parameters.AddWithValue("@BankTransactionTypeKeyPhrase", id);
            updateCmd.Parameters.AddWithValue("@KeyPhrase", keyPhrase);
            updateCmd.Parameters.AddWithValue("@IsActive", BankMappingHelper.ToActiveChar(request.IsActive));

            var rowsAffected = await updateCmd.ExecuteNonQueryAsync();
            if (rowsAffected == 0)
            {
                return new NotFoundObjectResult(new { message = "Bank key phrase not found." });
            }

            return new OkObjectResult(new
            {
                BankTransactionTypeKeyPhraseID = id,
                KeyPhrase = keyPhrase,
                IsActive = request.IsActive
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating bank key phrase {BankTransactionTypeKeyPhrase}.", id);
            return new StatusCodeResult(500);
        }
    }
}
