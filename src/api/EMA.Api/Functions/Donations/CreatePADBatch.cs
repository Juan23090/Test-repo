using System.Net;
using System.Text.Json;
using EMA.Api.Services.Security;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace EMA.Api.Functions.Donations;

public class CreatePADBatch
{
    private readonly string _connectionString;

    public CreatePADBatch(IConfiguration configuration)
    {
        _connectionString = configuration["SqlConnectionString"]
            ?? throw new InvalidOperationException("Missing SqlConnectionString.");
    }

    public class ParsedLineItem
    {
        public string UnalteredText { get; set; } = string.Empty;
        public DateTime? PADDate { get; set; }
        public string PADDonorName { get; set; } = string.Empty;
        public decimal PADDonationAmount { get; set; }
    }

    public class CreatePADBatchRequest
    {
        public int? EMAMethodOfGivingID { get; set; }
        public List<string> UnalteredLines { get; set; } = new();
        public List<ParsedLineItem>? ParsedLines { get; set; }
    }

    public class CreatePADBatchResponse
    {
        public int PADBatchID { get; set; }
        public int PADBatchStatusID { get; set; }
        public int LinesInserted { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public static int DetermineBatchInitialStatus(bool hasDuplicateDonations)
    {
        return hasDuplicateDonations ? 40 : 30;
    }

    [Function("CreatePADBatch")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "donations/pad-batch")] HttpRequestData req)
    {
        var identity = req.GetRequiredApiIdentity();
        int organizationId = identity.EMAOrganizationID;
        int userId = identity.UserID;

        var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        var request = JsonSerializer.Deserialize<CreatePADBatchRequest>(requestBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (request == null || organizationId <= 0)
        {
            var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
            await badResponse.WriteStringAsync("Invalid request payload.");
            return badResponse;
        }

        int newBatchId = 0;
        int linesInserted = 0;
        bool hasDuplicateDonations = false;
        int initialStatusID = 30;

        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();

            try
            {
                List<DateTime> duplicateDates = new();

                if (request.EMAMethodOfGivingID.HasValue && request.EMAMethodOfGivingID.Value > 0 && request.ParsedLines != null)
                {
                    var distinctDates = request.ParsedLines.Where(p => p.PADDate.HasValue)
                                                          .Select(p => p.PADDate!.Value.Date)
                                                          .Distinct()
                                                          .ToList();

                    if (distinctDates.Count > 0)
                    {
                        var checkParams = string.Join(",", distinctDates.Select((_, idx) => $"@d{idx}"));
                        var checkSql = $@"
                            SELECT DISTINCT CAST(DateTimeReceived AS DATE) AS ExistingDate
                            FROM dbo.Donation
                            WHERE EMAOrganizationID = @OrgID
                              AND EMAMethodOfGivingID = @MethodID
                              AND CAST(DateTimeReceived AS DATE) IN ({checkParams});";

                        using var checkCmd = new SqlCommand(checkSql, connection, transaction);
                        checkCmd.Parameters.AddWithValue("@OrgID", organizationId);
                        checkCmd.Parameters.AddWithValue("@MethodID", request.EMAMethodOfGivingID.Value);
                        for (int idx = 0; idx < distinctDates.Count; idx++)
                        {
                            checkCmd.Parameters.AddWithValue($"@d{idx}", distinctDates[idx]);
                        }

                        using var reader = await checkCmd.ExecuteReaderAsync();
                        while (await reader.ReadAsync())
                        {
                            duplicateDates.Add(reader.GetDateTime(0));
                        }
                        reader.Close();

                        if (duplicateDates.Count > 0)
                        {
                            hasDuplicateDonations = true;
                        }
                    }
                }

                initialStatusID = DetermineBatchInitialStatus(hasDuplicateDonations);

                var insertBatchSql = @"
                    INSERT INTO dbo.PADBatch (EMAOrganizationID, DateTimeCreated, CreatedByUserID, DateTimeUpdated, UpdatedByUserID, PADBatchStatusID)
                    OUTPUT INSERTED.PADBatchID
                    VALUES (@OrgID, GETUTCDATE(), @UserID, GETUTCDATE(), @UserID, @StatusID);";

                using (var batchCmd = new SqlCommand(insertBatchSql, connection, transaction))
                {
                    batchCmd.Parameters.AddWithValue("@OrgID", organizationId);
                    batchCmd.Parameters.AddWithValue("@UserID", userId);
                    batchCmd.Parameters.AddWithValue("@StatusID", initialStatusID);

                    var batchResult = await batchCmd.ExecuteScalarAsync();
                    newBatchId = Convert.ToInt32(batchResult);
                }

                var insertLineSql = @"
                    INSERT INTO dbo.PADBatchLine (EMAOrganizationID, PADBatchID, PADBatchLineID, UnalteredText, PADDate, PADDonorName, OfficialDonorName, PADDonationAmount)
                    VALUES (@OrgID, @BatchID, @LineID, @UnalteredText, @PADDate, @PADDonorName, '', @PADDonationAmount);";

                long lineIdCounter = 1;

                int itemCount = request.ParsedLines != null && request.ParsedLines.Count == request.UnalteredLines.Count
                    ? request.ParsedLines.Count
                    : request.UnalteredLines.Count;

                for (int i = 0; i < itemCount; i++)
                {
                    string unalteredText = request.UnalteredLines[i];
                    DateTime padDate = (request.ParsedLines != null && i < request.ParsedLines.Count ? request.ParsedLines[i].PADDate : null) ?? DateTime.Today;
                    string donorName = request.ParsedLines != null && i < request.ParsedLines.Count ? (request.ParsedLines[i].PADDonorName ?? string.Empty) : string.Empty;
                    decimal amount = request.ParsedLines != null && i < request.ParsedLines.Count ? request.ParsedLines[i].PADDonationAmount : 0m;

                    using var lineCmd = new SqlCommand(insertLineSql, connection, transaction);
                    lineCmd.Parameters.AddWithValue("@OrgID", organizationId);
                    lineCmd.Parameters.AddWithValue("@BatchID", newBatchId);
                    lineCmd.Parameters.AddWithValue("@LineID", lineIdCounter++);
                    lineCmd.Parameters.AddWithValue("@UnalteredText", unalteredText.Length > 256 ? unalteredText.Substring(0, 256) : unalteredText);
                    lineCmd.Parameters.AddWithValue("@PADDate", padDate);
                    lineCmd.Parameters.AddWithValue("@PADDonorName", donorName.Length > 128 ? donorName.Substring(0, 128) : donorName);
                    lineCmd.Parameters.AddWithValue("@PADDonationAmount", amount);

                    await lineCmd.ExecuteNonQueryAsync();
                    linesInserted++;
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new CreatePADBatchResponse
        {
            PADBatchID = newBatchId,
            PADBatchStatusID = initialStatusID,
            LinesInserted = linesInserted,
            Message = hasDuplicateDonations
                ? $"Transaction Already Processed - Must be ReImported. Batch #{newBatchId} status set to Aborted."
                : $"Batch #{newBatchId} created successfully with {linesInserted} line items stored in dbo.PADBatchLine."
        });

        return response;
    }
}
