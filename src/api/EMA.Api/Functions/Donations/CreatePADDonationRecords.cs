using System.Data;
using System.Net;
using System.Text.Json;
using EMA.Api.Services.Security;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Data.SqlClient;

namespace EMA.Api.Functions.Donations;

public class CreatePADDonationRecords
{
    private readonly string _connectionString;

    public CreatePADDonationRecords()
    {
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")
            ?? throw new InvalidOperationException("SqlConnectionString is not configured.");
    }

    public class BatchLineItem
    {
        public long PADBatchLineID { get; set; }
        public DateTime? PADDate { get; set; }
        public string PADDonorName { get; set; } = "";
        public string OfficialDonorName { get; set; } = "";
        public decimal PADDonationAmount { get; set; }
        public string UnalteredText { get; set; } = "";
        public int ResolvedDonorId { get; set; }
    }

    public const int ProcessableBatchStatusId = 30;

    public static bool IsBatchProcessable(int batchStatusId)
    {
        return batchStatusId == ProcessableBatchStatusId;
    }

    public static bool IsBatchAlreadyCompleted(int batchStatusId)
    {
        return batchStatusId == 60;
    }

    public static int ResolveDonorId(
        long lineId,
        string officialDonorName,
        Dictionary<long, int>? clientAssignments,
        HashSet<int>? validDonorIds,
        Dictionary<string, int>? donorMap)
    {
        if (clientAssignments != null &&
            clientAssignments.TryGetValue(lineId, out int explicitDonorId) &&
            explicitDonorId > 0 &&
            (validDonorIds == null || validDonorIds.Contains(explicitDonorId)))
        {
            return explicitDonorId;
        }

        if (donorMap != null && !string.IsNullOrWhiteSpace(officialDonorName))
        {
            string key = officialDonorName.Trim().ToLowerInvariant();
            if (donorMap.TryGetValue(key, out int matchedDonorId) && matchedDonorId > 0)
            {
                return matchedDonorId;
            }

            if (donorMap.TryGetValue(officialDonorName.Trim(), out int exactMatchedId) && exactMatchedId > 0)
            {
                return exactMatchedId;
            }
        }

        return 0;
    }

    public static string FormatFundOverrideComment(string defaultFundName, string overriddenFundName)
    {
        return $"Fund changed from {defaultFundName} to {overriddenFundName} during bank statement processing";
    }

    public static List<string> ValidateBatchLines(
        IEnumerable<BatchLineItem> lines,
        Dictionary<long, int>? clientAssignments,
        HashSet<int>? validDonorIds,
        Dictionary<string, int>? donorMap,
        HashSet<long>? deletedLineIds = null)
    {
        var validationErrors = new List<string>();

        foreach (var line in lines)
        {
            if (deletedLineIds != null && deletedLineIds.Contains(line.PADBatchLineID))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(line.OfficialDonorName) && (clientAssignments == null || !clientAssignments.ContainsKey(line.PADBatchLineID)))
            {
                validationErrors.Add($"Line {line.PADBatchLineID} ('{line.PADDonorName}') has not been matched to an official donor.");
                continue;
            }

            int resolvedDonorId = ResolveDonorId(line.PADBatchLineID, line.OfficialDonorName, clientAssignments, validDonorIds, donorMap);

            if (resolvedDonorId <= 0)
            {
                validationErrors.Add($"Official donor '{line.OfficialDonorName}' for line {line.PADBatchLineID} was not found in the donor directory.");
                continue;
            }

            line.ResolvedDonorId = resolvedDonorId;
        }

        return validationErrors;
    }

    [Function("CreatePADDonationRecords")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "donations/pad-create-donations")] HttpRequestData req)
    {
        var identity = req.GetRequiredApiIdentity();
        int organizationId = identity.EMAOrganizationID;
        int userId = identity.UserID;

        if (organizationId <= 0 || userId <= 0)
        {
            var errResp = req.CreateResponse(HttpStatusCode.BadRequest);
            await errResp.WriteStringAsync("Invalid authenticated identity.");
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
        var clientAssignments = new Dictionary<long, int>();
        var lineFundOverrides = new Dictionary<long, int>();
        var deletedLineIds = new HashSet<long>();

        foreach (var prop in root.EnumerateObject())
        {
            if (string.Equals(prop.Name, "PADBatchID", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(prop.Name, "batchID", StringComparison.OrdinalIgnoreCase))
            {
                batchId = prop.Value.GetInt32();
            }

            if (string.Equals(prop.Name, "LineDonorAssignments", StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in prop.Value.EnumerateObject())
                {
                    if (long.TryParse(entry.Name, out long lineId) && entry.Value.TryGetInt32(out int donorId))
                    {
                        clientAssignments[lineId] = donorId;
                    }
                }
            }

            if (string.Equals(prop.Name, "LineFundOverrides", StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in prop.Value.EnumerateObject())
                {
                    if (long.TryParse(entry.Name, out long lineId) && entry.Value.TryGetInt32(out int fundId))
                    {
                        lineFundOverrides[lineId] = fundId;
                    }
                }
            }

            if (string.Equals(prop.Name, "DeletedLineIDs", StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in prop.Value.EnumerateArray())
                {
                    if (item.TryGetInt64(out long delId))
                    {
                        deletedLineIds.Add(delId);
                    }
                }
            }
        }

        if (batchId <= 0)
        {
            var errResp = req.CreateResponse(HttpStatusCode.BadRequest);
            await errResp.WriteStringAsync("Invalid PADBatchID.");
            return errResp;
        }

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        var defaults = await GetOrganizationDefaultsAsync(conn, organizationId);
        if (!defaults.FundId.HasValue || defaults.FundId.Value <= 0)
        {
            var errResp = req.CreateResponse(HttpStatusCode.BadRequest);
            await errResp.WriteStringAsync("Organization default Fund for PADs is missing.");
            return errResp;
        }

        var (donorMap, validDonorIds) = await GetDonorMapAndValidIdsAsync(conn, organizationId);

        using var transaction = conn.BeginTransaction();
        try
        {
            int? currentBatchStatus = await GetBatchStatusAsync(conn, organizationId, batchId, transaction, lockRow: true);
            if (!currentBatchStatus.HasValue)
            {
                await transaction.RollbackAsync();
                var errResp = req.CreateResponse(HttpStatusCode.BadRequest);
                await errResp.WriteStringAsync($"Batch #{batchId} not found for this organization.");
                return errResp;
            }

            if (IsBatchAlreadyCompleted(currentBatchStatus.Value))
            {
                await transaction.RollbackAsync();
                var errResp = req.CreateResponse(HttpStatusCode.BadRequest);
                await errResp.WriteStringAsync($"Donation records have already been created for Batch #{batchId}.");
                return errResp;
            }

            if (!IsBatchProcessable(currentBatchStatus.Value))
            {
                await transaction.RollbackAsync();
                var errResp = req.CreateResponse(HttpStatusCode.BadRequest);
                await errResp.WriteStringAsync($"Batch #{batchId} cannot be processed because its status is {currentBatchStatus.Value} (expected status {ProcessableBatchStatusId}).");
                return errResp;
            }

            var lines = await GetBatchLinesAsync(conn, organizationId, batchId, transaction);
            if (lines.Count == 0)
            {
                await transaction.RollbackAsync();
                var errResp = req.CreateResponse(HttpStatusCode.BadRequest);
                await errResp.WriteStringAsync("No lines found for the specified batch.");
                return errResp;
            }

            var validationErrors = ValidateBatchLines(lines, clientAssignments, validDonorIds, donorMap, deletedLineIds);
            if (validationErrors.Count > 0)
            {
                await transaction.RollbackAsync();
                var errResp = req.CreateResponse(HttpStatusCode.BadRequest);
                await errResp.WriteStringAsync(string.Join("\n", validationErrors));
                return errResp;
            }
            int createdCount = 0;

            var fundNames = new Dictionary<int, string>();
            string fundsSql = @"SELECT EMAFundID, Name FROM dbo.OrganizationFund WHERE EMAOrganizationID = @OrgID;";
            using (var fundsCmd = new SqlCommand(fundsSql, conn, transaction))
            {
                fundsCmd.Parameters.AddWithValue("@OrgID", organizationId);
                using var fundsReader = await fundsCmd.ExecuteReaderAsync();
                while (await fundsReader.ReadAsync())
                {
                    fundNames[fundsReader.GetInt32(0)] = fundsReader.GetString(1);
                }
            }

            foreach (var line in lines)
            {
                if (deletedLineIds.Contains(line.PADBatchLineID))
                {
                    continue;
                }

                string commentText;
                if (lineFundOverrides.TryGetValue(line.PADBatchLineID, out int overriddenFundId) &&
                    overriddenFundId > 0 &&
                    defaults.FundId.HasValue &&
                    overriddenFundId != defaults.FundId.Value)
                {
                    string defaultFundName = fundNames.TryGetValue(defaults.FundId.Value, out var dfn) ? dfn : "General Donations";
                    string newFundName = fundNames.TryGetValue(overriddenFundId, out var nfn) ? nfn : $"Fund #{overriddenFundId}";
                    commentText = FormatFundOverrideComment(defaultFundName, newFundName);
                }
                else
                {
                    commentText = string.IsNullOrWhiteSpace(line.UnalteredText) ? $"PAD Batch #{batchId} Line #{line.PADBatchLineID}" : line.UnalteredText;
                }

                string insertDonationSql = @"
                    INSERT INTO dbo.Donation
                    (
                        EMAOrganizationID,
                        EMADonorID,
                        Comments,
                        DateTimeReceived,
                        EMADonationSourceID,
                        EMAEventID,
                        EMAMethodOfGivingID,
                        InteracDonorName,
                        ReferenceNumber,
                        DonationAmount,
                        IsTemplate,
                        DateTimeCreated,
                        CreatedbyUserID
                    )
                    VALUES
                    (
                        @OrgID,
                        @DonorID,
                        @Comments,
                        @DateReceived,
                        @SourceID,
                        @EventID,
                        @MethodID,
                        @InteracName,
                        @RefNum,
                        @Amount,
                        'N',
                        GETUTCDATE(),
                        @UserID
                    );
                    SELECT CAST(SCOPE_IDENTITY() AS BIGINT);";

                using var insertDonationCmd = new SqlCommand(insertDonationSql, conn, transaction);
                insertDonationCmd.Parameters.AddWithValue("@OrgID", organizationId);
                insertDonationCmd.Parameters.AddWithValue("@DonorID", line.ResolvedDonorId);
                insertDonationCmd.Parameters.AddWithValue("@Comments", commentText);
                insertDonationCmd.Parameters.AddWithValue("@DateReceived", (object?)line.PADDate ?? DateTime.UtcNow);
                insertDonationCmd.Parameters.AddWithValue("@SourceID", (object?)defaults.SourceId ?? DBNull.Value);
                insertDonationCmd.Parameters.AddWithValue("@EventID", (object?)defaults.EventId ?? DBNull.Value);
                insertDonationCmd.Parameters.AddWithValue("@MethodID", (object?)defaults.MethodId ?? DBNull.Value);
                insertDonationCmd.Parameters.AddWithValue("@InteracName", (object?)line.PADDonorName ?? DBNull.Value);
                insertDonationCmd.Parameters.AddWithValue("@RefNum", $"PAD-B{batchId}-L{line.PADBatchLineID}");
                insertDonationCmd.Parameters.AddWithValue("@Amount", line.PADDonationAmount);
                insertDonationCmd.Parameters.AddWithValue("@UserID", userId);

                object? newDonationIdObj = await insertDonationCmd.ExecuteScalarAsync();
                long donationId = Convert.ToInt64(newDonationIdObj);

                using var spCmd = new SqlCommand("dbo.sp_ProcessBankDonationDistributions", conn, transaction);
                spCmd.CommandType = CommandType.StoredProcedure;
                spCmd.Parameters.AddWithValue("@EMADonationID", donationId);
                await spCmd.ExecuteNonQueryAsync();

                string resolvedName = donorMap.FirstOrDefault(x => x.Value == line.ResolvedDonorId).Key ?? line.OfficialDonorName;
                string updateLineSql = @"
                    UPDATE dbo.PADBatchLine
                    SET OfficialDonorName = @OfficialName
                    WHERE EMAOrganizationID = @OrgID 
                      AND PADBatchID = @BatchID 
                      AND PADBatchLineID = @LineID;";

                using var updateLineCmd = new SqlCommand(updateLineSql, conn, transaction);
                updateLineCmd.Parameters.AddWithValue("@OfficialName", string.IsNullOrEmpty(resolvedName) ? (object)DBNull.Value : resolvedName);
                updateLineCmd.Parameters.AddWithValue("@OrgID", organizationId);
                updateLineCmd.Parameters.AddWithValue("@BatchID", batchId);
                updateLineCmd.Parameters.AddWithValue("@LineID", line.PADBatchLineID);

                await updateLineCmd.ExecuteNonQueryAsync();
                createdCount++;
            }

            string updateBatchSql = @"
                UPDATE dbo.PADBatch
                SET PADBatchStatusID = 60,
                    DateTimeUpdated = GETUTCDATE(),
                    UpdatedByUserID = @UserID
                WHERE PADBatchID = @BatchID AND EMAOrganizationID = @OrgID;";

            using var updateBatchCmd = new SqlCommand(updateBatchSql, conn, transaction);
            updateBatchCmd.Parameters.AddWithValue("@UserID", userId);
            updateBatchCmd.Parameters.AddWithValue("@BatchID", batchId);
            updateBatchCmd.Parameters.AddWithValue("@OrgID", organizationId);

            await updateBatchCmd.ExecuteNonQueryAsync();
            await transaction.CommitAsync();

            var resultPayload = new
            {
                Success = true,
                PADBatchID = batchId,
                PADBatchStatusID = 60,
                DonationsCreated = createdCount,
                Message = $"Successfully created {createdCount} donation records from PAD Batch #{batchId}."
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(resultPayload));
            return response;
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            var errResp = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errResp.WriteStringAsync("Failed to create donation records.");
            return errResp;
        }
    }

    private async Task<int?> GetBatchStatusAsync(SqlConnection conn, int organizationId, int batchId, SqlTransaction? transaction = null, bool lockRow = false)
    {
        string lockHint = lockRow ? "WITH (UPDLOCK, HOLDLOCK)" : "";
        string sql = $@"
            SELECT PADBatchStatusID
            FROM dbo.PADBatch {lockHint}
            WHERE EMAOrganizationID = @OrgID AND PADBatchID = @BatchID;";

        using var cmd = new SqlCommand(sql, conn, transaction);
        cmd.Parameters.AddWithValue("@OrgID", organizationId);
        cmd.Parameters.AddWithValue("@BatchID", batchId);

        object? result = await cmd.ExecuteScalarAsync();
        if (result == null || result == DBNull.Value) return null;
        return Convert.ToInt32(result);
    }

    private async Task<(int? FundId, int? SourceId, int? MethodId, int? EventId, int? CampusId)> GetOrganizationDefaultsAsync(SqlConnection conn, int organizationId)
    {
        int? fundId = null;
        int? sourceId = null;
        int? methodId = null;
        int? eventId = null;
        int? campusId = null;

        string orgSql = @"
            SELECT 
                EMAFundIDForPADs,
                EMASourceIDForPADs,
                EMAMethodOfGivingIDForPADs,
                EMAEventIDForPADs,
                EMACampusIDForPADs
            FROM dbo.Organization
            WHERE EMAOrganizationID = @OrgID;";

        using var orgCmd = new SqlCommand(orgSql, conn);
        orgCmd.Parameters.AddWithValue("@OrgID", organizationId);
        using var reader = await orgCmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            if (!reader.IsDBNull(0)) fundId = reader.GetInt32(0);
            if (!reader.IsDBNull(1)) sourceId = reader.GetInt32(1);
            if (!reader.IsDBNull(2)) methodId = reader.GetInt32(2);
            if (!reader.IsDBNull(3)) eventId = reader.GetInt32(3);
            if (!reader.IsDBNull(4)) campusId = reader.GetInt32(4);
        }
        reader.Close();

        if (!fundId.HasValue || fundId.Value <= 0)
        {
            string fallbackFundSql = @"
                SELECT TOP 1 EMAFundID
                FROM dbo.OrganizationFund
                WHERE EMAOrganizationID = @OrgID AND IsActive = 'Y'
                ORDER BY Sequence ASC, EMAFundID ASC;";

            using var fundCmd = new SqlCommand(fallbackFundSql, conn);
            fundCmd.Parameters.AddWithValue("@OrgID", organizationId);
            object? fundResult = await fundCmd.ExecuteScalarAsync();
            if (fundResult != null && fundResult != DBNull.Value)
            {
                fundId = Convert.ToInt32(fundResult);
            }
        }

        return (fundId, sourceId, methodId, eventId, campusId);
    }

    private async Task<List<BatchLineItem>> GetBatchLinesAsync(SqlConnection conn, int organizationId, int batchId, SqlTransaction? transaction = null)
    {
        var list = new List<BatchLineItem>();

        string sql = @"
            SELECT PADBatchLineID, PADDate, PADDonorName, OfficialDonorName, PADDonationAmount, UnalteredText
            FROM dbo.PADBatchLine
            WHERE EMAOrganizationID = @OrgID AND PADBatchID = @BatchID
            ORDER BY PADBatchLineID ASC;";

        using var cmd = new SqlCommand(sql, conn, transaction);
        cmd.Parameters.AddWithValue("@OrgID", organizationId);
        cmd.Parameters.AddWithValue("@BatchID", batchId);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new BatchLineItem
            {
                PADBatchLineID = Convert.ToInt64(reader["PADBatchLineID"]),
                PADDate = reader.IsDBNull(reader.GetOrdinal("PADDate")) ? null : Convert.ToDateTime(reader["PADDate"]),
                PADDonorName = reader.IsDBNull(reader.GetOrdinal("PADDonorName")) ? "" : reader["PADDonorName"].ToString() ?? "",
                OfficialDonorName = reader.IsDBNull(reader.GetOrdinal("OfficialDonorName")) ? "" : reader["OfficialDonorName"].ToString() ?? "",
                PADDonationAmount = reader.IsDBNull(reader.GetOrdinal("PADDonationAmount")) ? 0m : Convert.ToDecimal(reader["PADDonationAmount"]),
                UnalteredText = reader.IsDBNull(reader.GetOrdinal("UnalteredText")) ? "" : reader["UnalteredText"].ToString() ?? ""
            });
        }

        return list;
    }

    private async Task<(Dictionary<string, int> Map, HashSet<int> ValidIds)> GetDonorMapAndValidIdsAsync(SqlConnection conn, int organizationId)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var validIds = new HashSet<int>();

        string sql = @"
            SELECT EMADonorID, OfficialDonorName, InteracDonorName
            FROM dbo.Donor
            WHERE EMAOrganizationID = @OrgID;";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@OrgID", organizationId);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            int donorId = Convert.ToInt32(reader.GetValue(0));
            validIds.Add(donorId);

            string officialName = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim();
            string interacName = reader.IsDBNull(2) ? "" : reader.GetString(2).Trim();

            if (!string.IsNullOrEmpty(officialName) && !map.ContainsKey(officialName))
            {
                map[officialName] = donorId;
            }

            if (!string.IsNullOrEmpty(interacName) && !map.ContainsKey(interacName))
            {
                map[interacName] = donorId;
            }
        }

        return (map, validIds);
    }
}
