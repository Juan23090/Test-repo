using System.Data;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using EMA.Api.Services.Security;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Data.SqlClient;

namespace EMA.Api.Functions.Donations;

public class MatchPADDonors
{
    private readonly string _connectionString;

    public MatchPADDonors()
    {
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")
            ?? throw new InvalidOperationException("SqlConnectionString is not configured.");
    }

    [Function("MatchPADDonors")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "donations/pad-match-donors")] HttpRequestData req)
    {
        var identity = req.GetRequiredApiIdentity();
        int organizationId = identity.EMAOrganizationID;

        if (organizationId <= 0)
        {
            var errResp = req.CreateResponse(HttpStatusCode.BadRequest);
            await errResp.WriteStringAsync("Invalid EMAOrganizationID.");
            return errResp;
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");

        string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(requestBody))
        {
            var errResp = req.CreateResponse(HttpStatusCode.BadRequest);
            await errResp.WriteStringAsync("Request body cannot be empty.");
            return errResp;
        }

        using var doc = JsonDocument.Parse(requestBody);
        var root = doc.RootElement;

        int batchId = 0;

        foreach (var prop in root.EnumerateObject())
        {
            if (string.Equals(prop.Name, "PADBatchID", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(prop.Name, "batchID", StringComparison.OrdinalIgnoreCase))
            {
                batchId = prop.Value.GetInt32();
            }
        }

        if (batchId <= 0)
        {
            var errResp = req.CreateResponse(HttpStatusCode.BadRequest);
            await errResp.WriteStringAsync("Invalid PADBatchID.");
            return errResp;
        }

        bool batchExists = await VerifyBatchOwnershipAsync(organizationId, batchId);
        if (!batchExists)
        {
            var notFoundResp = req.CreateResponse(HttpStatusCode.BadRequest);
            await notFoundResp.WriteStringAsync($"Batch #{batchId} not found for this organization.");
            return notFoundResp;
        }

        var donors = await FetchOrganizationDonorsAsync(organizationId);
        var batchLines = await FetchPADBatchLinesAsync(organizationId, batchId);

        var matchResults = new List<PADLineMatchResult>();

        foreach (var line in batchLines)
        {
            var lineResult = EvaluateLineMatching(line, donors);
            matchResults.Add(lineResult);
            if (!string.IsNullOrEmpty(lineResult.MatchedDonorName) && lineResult.IsAutoMatched && !lineResult.IsAmbiguous)
            {
                await UpdateLineMatchInDatabaseAsync(organizationId, batchId, line.PADBatchLineID, lineResult.MatchedDonorName);
            }
        }

        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        await response.WriteStringAsync(JsonSerializer.Serialize(matchResults, jsonOptions));
        return response;
    }

    private async Task<bool> VerifyBatchOwnershipAsync(int organizationId, int batchId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        string sql = "SELECT COUNT(1) FROM dbo.PADBatch WHERE PADBatchID = @BatchID AND EMAOrganizationID = @OrgID;";
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@BatchID", batchId);
        cmd.Parameters.AddWithValue("@OrgID", organizationId);

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result) > 0;
    }

    private async Task<List<DonorRecord>> FetchOrganizationDonorsAsync(int organizationId)
    {
        var list = new List<DonorRecord>();
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        string sql = @"
            SELECT EMADonorID, OfficialDonorName, InteracDonorName, EmailAddress, GivingNumber
            FROM dbo.Donor
            WHERE EMAOrganizationID = @OrgID;";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@OrgID", organizationId);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            string officialName = reader.IsDBNull(1) ? "" : reader.GetString(1);
            string interacName = reader.IsDBNull(2) ? "" : reader.GetString(2);
            string displayName = !string.IsNullOrWhiteSpace(officialName) ? officialName : interacName;
            string email = reader.IsDBNull(3) ? "" : reader.GetString(3);
            string givingNum = reader.IsDBNull(4) ? "" : reader.GetString(4);

            list.Add(new DonorRecord
            {
                EMADonorID = Convert.ToInt32(reader.GetValue(0)),
                OfficialDonorName = displayName,
                Email = email,
                EnvelopeNumber = givingNum
            });
        }

        return list;
    }

    private async Task<List<PADLineRecord>> FetchPADBatchLinesAsync(int organizationId, int batchId)
    {
        var list = new List<PADLineRecord>();
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        string sql = @"
            SELECT PADBatchLineID, PADBatchID, PADDate AS Date, PADDonorName, PADDonationAmount AS DonationAmount, OfficialDonorName, UnalteredText
            FROM dbo.PADBatchLine
            WHERE PADBatchID = @BatchID AND EMAOrganizationID = @OrgID
            ORDER BY PADBatchLineID ASC;";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@BatchID", batchId);
        cmd.Parameters.AddWithValue("@OrgID", organizationId);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new PADLineRecord
            {
                PADBatchLineID = Convert.ToInt32(reader.GetValue(0)),
                PADBatchID = Convert.ToInt32(reader.GetValue(1)),
                Date = reader.IsDBNull(2) ? null : reader.GetDateTime(2),
                PADDonorName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                DonationAmount = reader.IsDBNull(4) ? 0m : Convert.ToDecimal(reader.GetValue(4)),
                OfficialDonorName = reader.IsDBNull(5) ? "" : reader.GetString(5),
                UnalteredText = reader.IsDBNull(6) ? "" : reader.GetString(6)
            });
        }

        return list;
    }

    private async Task UpdateLineMatchInDatabaseAsync(int organizationId, int batchId, int padBatchLineId, string matchedDonorName)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        string sql = @"
            UPDATE dbo.PADBatchLine
            SET OfficialDonorName = @OfficialDonorName
            WHERE EMAOrganizationID = @OrgID AND PADBatchID = @PADBatchID AND PADBatchLineID = @PADBatchLineID;";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@OfficialDonorName", (object?)matchedDonorName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@OrgID", organizationId);
        cmd.Parameters.AddWithValue("@PADBatchID", batchId);
        cmd.Parameters.AddWithValue("@PADBatchLineID", padBatchLineId);

        await cmd.ExecuteNonQueryAsync();
    }

    public static PADLineMatchResult EvaluateLineMatching(PADLineRecord line, List<DonorRecord> donors)
    {
        var result = new PADLineMatchResult
        {
            PADBatchLineID = line.PADBatchLineID,
            PADDonorName = line.PADDonorName,
            Date = line.Date,
            DonationAmount = line.DonationAmount,
            UnalteredText = line.UnalteredText,
            MatchedDonorName = line.OfficialDonorName
        };

        if (!string.IsNullOrWhiteSpace(line.OfficialDonorName))
        {
            var alreadyMatchedDonor = donors.FirstOrDefault(d => string.Equals(d.OfficialDonorName, line.OfficialDonorName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (alreadyMatchedDonor != null)
            {
                result.MatchedDonorID = alreadyMatchedDonor.EMADonorID;
                result.MatchedDonorName = alreadyMatchedDonor.OfficialDonorName;
                result.MatchStatus = "Selected";
                result.IsAutoMatched = true;
                result.IsAmbiguous = false;
                return result;
            }
        }

        var lineTokens = TokenizeName(line.PADDonorName);
        if (lineTokens.Count == 0 || donors.Count == 0)
        {
            result.MatchStatus = "No Match";
            return result;
        }

        int maxMatchCount = 0;
        var donorScores = new List<(DonorRecord Donor, int Score)>();

        foreach (var donor in donors)
        {
            var donorTokens = TokenizeName(donor.OfficialDonorName);
            int matchCount = lineTokens.Count(t => donorTokens.Contains(t, StringComparer.OrdinalIgnoreCase));

            if (matchCount > 0)
            {
                donorScores.Add((donor, matchCount));
                if (matchCount > maxMatchCount)
                {
                    maxMatchCount = matchCount;
                }
            }
        }

        if (maxMatchCount == 0)
        {
            result.MatchStatus = "No Match";
            return result;
        }

        var topMatches = donorScores.Where(ds => ds.Score == maxMatchCount).Select(ds => ds.Donor).ToList();

        if (topMatches.Count == 1)
        {
            var topDonor = topMatches[0];
            result.MatchedDonorID = topDonor.EMADonorID;
            result.MatchedDonorName = topDonor.OfficialDonorName;
            result.MatchStatus = "Auto-Matched";
            result.IsAutoMatched = true;
        }
        else
        {
            result.MatchStatus = "Ambiguous (Ties)";
            result.IsAmbiguous = true;
            result.CandidateDonors = topMatches.Select(d => new CandidateDonorDto
            {
                EMADonorID = d.EMADonorID,
                FullName = d.OfficialDonorName,
                Email = d.Email,
                EnvelopeNumber = d.EnvelopeNumber,
                MatchScore = maxMatchCount
            }).ToList();
        }

        return result;
    }

    public static HashSet<string> TokenizeName(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return new HashSet<string>();
        string cleaned = Regex.Replace(input, @"[^A-Za-z&\s]", " ");
        var words = cleaned.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                           .Where(w => w.Length > 1 || w == "&")
                           .Select(w => w.Trim())
                           .ToList();

        return new HashSet<string>(words, StringComparer.OrdinalIgnoreCase);
    }
}

public class DonorRecord
{
    public int EMADonorID { get; set; }
    public string OfficialDonorName { get; set; } = "";
    public string Email { get; set; } = "";
    public string EnvelopeNumber { get; set; } = "";
}

public class PADLineRecord
{
    public int PADBatchLineID { get; set; }
    public int PADBatchID { get; set; }
    public DateTime? Date { get; set; }
    public string PADDonorName { get; set; } = "";
    public decimal DonationAmount { get; set; }
    public string OfficialDonorName { get; set; } = "";
    public string UnalteredText { get; set; } = "";
}

public class PADLineMatchResult
{
    public int PADBatchLineID { get; set; }
    public string PADDonorName { get; set; } = "";
    public DateTime? Date { get; set; }
    public decimal DonationAmount { get; set; }
    public string UnalteredText { get; set; } = "";
    public int? MatchedDonorID { get; set; }
    public string MatchedDonorName { get; set; } = "";
    public string MatchStatus { get; set; } = "No Match";
    public bool IsAutoMatched { get; set; }
    public bool IsAmbiguous { get; set; }
    public List<CandidateDonorDto> CandidateDonors { get; set; } = new();
}

public class CandidateDonorDto
{
    public int EMADonorID { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string EnvelopeNumber { get; set; } = "";
    public int MatchScore { get; set; }
}
