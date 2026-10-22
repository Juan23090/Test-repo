using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace EMA.Api.Functions.Donations;

public class GetPADKeywords
{
    private readonly string _connectionString;

    public GetPADKeywords(IConfiguration configuration)
    {
        _connectionString = configuration["SqlConnectionString"]
            ?? throw new InvalidOperationException("Missing SqlConnectionString.");
    }

    [Function("GetPADKeywords")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "donations/pad-keywords")] HttpRequestData req)
    {
        var keyphrases = new List<BankKeyPhraseItem>();
        var queryParams = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        string? bankId = queryParams["bankId"];

        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            var sql = @"
                SELECT kp.KeyPhrase, LTRIM(RTRIM(btt.TransactionTypeID)) AS TransactionTypeID
                FROM dbo.BankTransactionTypeKeyPhrase kp
                JOIN dbo.BankTransactionType btt ON kp.BankTransactionTypeID = btt.BankTransactionTypeID
                WHERE kp.IsActive = 'Y' AND (@BankID IS NULL OR btt.BankId = @BankID)
                UNION
                SELECT Keyword AS KeyPhrase, 'PAD' AS TransactionTypeID
                FROM dbo.PADKeywords";

            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@BankID", string.IsNullOrWhiteSpace(bankId) ? DBNull.Value : bankId.Trim());
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                if (!reader.IsDBNull(0))
                {
                    string phrase = reader.GetString(0).Trim();
                    string type = reader.IsDBNull(1) ? "PAD" : reader.GetString(1).Trim();
                    keyphrases.Add(new BankKeyPhraseItem
                    {
                        KeyPhrase = phrase,
                        TransactionTypeID = type
                    });
                }
            }
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(keyphrases);
        return response;
    }

    [Function("GetBanks")]
    public async Task<HttpResponseData> RunGetBanks(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "donations/banks")] HttpRequestData req)
    {
        var banks = new List<BankItemDto>();

        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            var sql = @"
                SELECT RTRIM(b.BankID) AS BankID, 
                       RTRIM(b.Name) AS Name,
                       MAX(RTRIM(ISNULL(btt.InstructionsURL, ''))) AS InstructionsURL
                FROM dbo.Bank b
                LEFT JOIN dbo.BankTransactionType btt ON b.BankID = btt.BankId AND btt.IsActive = 'Y'
                WHERE b.IsActive = 'Y'
                GROUP BY b.BankID, b.Name
                ORDER BY b.Name";

            using var command = new SqlCommand(sql, connection);
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                banks.Add(new BankItemDto
                {
                    BankID = reader.GetString(0),
                    Name = reader.GetString(1),
                    InstructionsURL = !reader.IsDBNull(2) ? reader.GetString(2) : string.Empty
                });
            }
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(banks);
        return response;
    }

    public class BankKeyPhraseItem
    {
        public string KeyPhrase { get; set; } = string.Empty;
        public string TransactionTypeID { get; set; } = "PAD";
    }

    public class BankItemDto
    {
        public string BankID { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? InstructionsURL { get; set; }
    }
}
