using EMA.Api.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Banks;

public class GetBankLookups
{
    private readonly ILogger<GetBankLookups> _logger;
    private readonly string _connectionString;

    public GetBankLookups(ILogger<GetBankLookups> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("GetBankLookups")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "banks/lookups")] HttpRequest req)
    {
        _logger.LogInformation("GetBankLookups triggered.");

        var response = new BankLookupsResponseDto();

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            const string sql = @"
                SELECT RTRIM(BankID) AS BankID, RTRIM(Name) AS Name, IsActive 
                FROM dbo.Bank 
                ORDER BY Name;

                SELECT RTRIM(TransactionTypeID) AS TransactionTypeID, RTRIM(Description) AS Description 
                FROM dbo.TransactionType 
                ORDER BY Description;

                SELECT btt.BankTransactionTypeID, RTRIM(btt.BankId) AS BankId, RTRIM(b.Name) AS BankName, 
                       RTRIM(btt.TransactionTypeID) AS TransactionTypeID, RTRIM(tt.Description) AS TransactionTypeDescription, 
                       btt.InstructionsURL, btt.IsActive 
                FROM dbo.BankTransactionType btt 
                LEFT JOIN dbo.Bank b ON btt.BankId = b.BankID 
                LEFT JOIN dbo.TransactionType tt ON btt.TransactionTypeID = tt.TransactionTypeID 
                ORDER BY b.Name, tt.Description;

                SELECT kp.BankTransactionTypeKeyPhrase AS BankTransactionTypeKeyPhraseID, kp.BankTransactionTypeID, 
                       RTRIM(btt.BankId) AS BankId, RTRIM(b.Name) AS BankName, 
                       RTRIM(btt.TransactionTypeID) AS TransactionTypeID, kp.KeyPhrase, kp.IsActive 
                FROM dbo.BankTransactionTypeKeyPhrase kp 
                JOIN dbo.BankTransactionType btt ON kp.BankTransactionTypeID = btt.BankTransactionTypeID 
                LEFT JOIN dbo.Bank b ON btt.BankId = b.BankID 
                LEFT JOIN dbo.TransactionType tt ON btt.TransactionTypeID = tt.TransactionTypeID 
                ORDER BY b.Name, tt.Description, kp.KeyPhrase;
            ";

            using var cmd = new SqlCommand(sql, conn);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                response.Banks.Add(new BankDto
                {
                    BankID = reader.GetString(0),
                    Name = reader.GetString(1),
                    IsActive = BankMappingHelper.ToBool(reader.GetString(2))
                });
            }

            if (await reader.NextResultAsync())
            {
                while (await reader.ReadAsync())
                {
                    response.TransactionTypes.Add(new TransactionTypeDto
                    {
                        TransactionTypeID = reader.GetString(0),
                        Description = reader.GetString(1)
                    });
                }
            }

            if (await reader.NextResultAsync())
            {
                while (await reader.ReadAsync())
                {
                    response.BankTransactionTypes.Add(new BankTransactionTypeDto
                    {
                        BankTransactionTypeID = reader.GetInt32(0),
                        BankId = reader.GetString(1),
                        BankName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                        TransactionTypeID = reader.GetString(3),
                        TransactionTypeDescription = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                        InstructionsURL = reader.IsDBNull(5) ? null : reader.GetString(5),
                        IsActive = BankMappingHelper.ToBool(reader.GetString(6))
                    });
                }
            }

            if (await reader.NextResultAsync())
            {
                while (await reader.ReadAsync())
                {
                    response.KeyPhrases.Add(new BankKeyPhraseDto
                    {
                        BankTransactionTypeKeyPhraseID = reader.GetInt32(0),
                        BankTransactionTypeID = reader.GetInt32(1),
                        BankId = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                        BankName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                        TransactionTypeID = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                        KeyPhrase = reader.GetString(5),
                        IsActive = BankMappingHelper.ToBool(reader.GetString(6))
                    });
                }
            }

            return new OkObjectResult(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching bank lookups.");
            return new StatusCodeResult(500);
        }
    }
}
