using System.Text.Json;
using EMA.Api.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Banks;

public class SaveBank
{
    private readonly ILogger<SaveBank> _logger;
    private readonly string _connectionString;

    public SaveBank(ILogger<SaveBank> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("PostBank")]
    public async Task<IActionResult> Post(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "banks")] HttpRequest req)
    {
        _logger.LogInformation("PostBank triggered.");

        CreateBankRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<CreateBankRequest>(
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

        var bankId = BankMappingHelper.TrimOrEmpty(request.BankID);
        var name = BankMappingHelper.TrimOrEmpty(request.Name);

        if (string.IsNullOrWhiteSpace(bankId) || bankId.Length > 6)
        {
            return new BadRequestObjectResult(new { message = "Bank ID is required and must be 6 characters or fewer." });
        }

        if (string.IsNullOrWhiteSpace(name) || name.Length > 50)
        {
            return new BadRequestObjectResult(new { message = "Bank Name is required and must be 50 characters or fewer." });
        }

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var checkCmd = new SqlCommand("SELECT COUNT(1) FROM dbo.Bank WHERE BankID = @BankID", conn);
            checkCmd.Parameters.AddWithValue("@BankID", bankId);
            var exists = (int?)await checkCmd.ExecuteScalarAsync() > 0;

            if (exists)
            {
                return new ConflictObjectResult(new { message = "A bank with this Bank ID already exists." });
            }

            using var insertCmd = new SqlCommand(
                "INSERT INTO dbo.Bank (BankID, Name, IsActive) VALUES (@BankID, @Name, @IsActive)",
                conn);
            insertCmd.Parameters.AddWithValue("@BankID", bankId);
            insertCmd.Parameters.AddWithValue("@Name", name);
            insertCmd.Parameters.AddWithValue("@IsActive", BankMappingHelper.ToActiveChar(request.IsActive));

            await insertCmd.ExecuteNonQueryAsync();

            return new OkObjectResult(new BankDto
            {
                BankID = bankId,
                Name = name,
                IsActive = request.IsActive
            });
        }
        catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
        {
            _logger.LogWarning(ex, "Duplicate BankID on insert: {BankID}", bankId);
            return new ConflictObjectResult(new { message = "A bank with this Bank ID already exists." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating bank.");
            return new StatusCodeResult(500);
        }
    }

    [Function("PutBank")]
    public async Task<IActionResult> Put(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "banks/{bankId}")] HttpRequest req,
        string bankId)
    {
        _logger.LogInformation("PutBank triggered for {BankID}.", bankId);

        UpdateBankRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<UpdateBankRequest>(
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

        var trimmedBankId = BankMappingHelper.TrimOrEmpty(bankId);
        var name = BankMappingHelper.TrimOrEmpty(request.Name);

        if (string.IsNullOrWhiteSpace(trimmedBankId) || trimmedBankId.Length > 6)
        {
            return new BadRequestObjectResult(new { message = "Bank ID is invalid." });
        }

        if (string.IsNullOrWhiteSpace(name) || name.Length > 50)
        {
            return new BadRequestObjectResult(new { message = "Bank Name is required and must be 50 characters or fewer." });
        }

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var updateCmd = new SqlCommand(
                "UPDATE dbo.Bank SET Name = @Name, IsActive = @IsActive WHERE BankID = @BankID",
                conn);
            updateCmd.Parameters.AddWithValue("@BankID", trimmedBankId);
            updateCmd.Parameters.AddWithValue("@Name", name);
            updateCmd.Parameters.AddWithValue("@IsActive", BankMappingHelper.ToActiveChar(request.IsActive));

            var rowsAffected = await updateCmd.ExecuteNonQueryAsync();
            if (rowsAffected == 0)
            {
                return new NotFoundObjectResult(new { message = "Bank not found." });
            }

            return new OkObjectResult(new BankDto
            {
                BankID = trimmedBankId,
                Name = name,
                IsActive = request.IsActive
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating bank {BankID}.", bankId);
            return new StatusCodeResult(500);
        }
    }
}
