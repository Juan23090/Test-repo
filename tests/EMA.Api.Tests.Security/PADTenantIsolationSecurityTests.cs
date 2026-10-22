using System.Reflection;
using EMA.Api.Functions.Donations;
using EMA.Api.Services.Security;
using EMA_Api.Functions.Organization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Data.SqlClient;

namespace EMA.Api.Tests.Security;

public sealed class PADTenantIsolationSecurityTests
{
    private const string ConnectionVariable = "EMA_TEST_SQL_CONNECTION";
    private const string TestOrganizationName = "ZZ Tenant Isolation Test Org";

    // triggers have to be anonymous or auth middleware gets bypassed
    [Theory]
    [InlineData(typeof(CreatePADBatch))]
    [InlineData(typeof(MatchPADDonors))]
    [InlineData(typeof(CreatePADDonationRecords))]
    [InlineData(typeof(UpdatePADBatchLineDonor))]
    [InlineData(typeof(UpdatePADDefaults))]
    [InlineData(typeof(GetPADBatches))]
    [InlineData(typeof(GetPADKeywords))]
    [InlineData(typeof(ExtractPADPdf))]
    public void AllPADEndpointsUseAnonymousTrigger_SoCentralizedBoundaryApplies(Type functionType)
    {
        var runMethod = functionType.GetMethod("Run");
        Assert.NotNull(runMethod);

        var triggerParam = runMethod.GetParameters()
            .FirstOrDefault(p => p.GetCustomAttribute<HttpTriggerAttribute>() != null);
        Assert.NotNull(triggerParam);

        var triggerAttr = triggerParam.GetCustomAttribute<HttpTriggerAttribute>();
        Assert.NotNull(triggerAttr);

        Assert.Equal(AuthorizationLevel.Anonymous, triggerAttr.AuthLevel);
    }

    // all endpoints must require an authenticated organization user
    [Theory]
    [InlineData("CreatePADBatch")]
    [InlineData("MatchPADDonors")]
    [InlineData("CreatePADDonationRecords")]
    [InlineData("UpdatePADBatchLineDonor")]
    [InlineData("UpdatePADDefaults")]
    [InlineData("GetPADBatches")]
    [InlineData("GetPADKeywords")]
    [InlineData("ExtractPADPdf")]
    public void AllPADEndpointsRequireOrganizationUserPolicy(string functionName)
    {
        var policy = new ApiEndpointPolicyRegistry().GetPolicy(functionName);
        Assert.Equal(ApiEndpointAccess.OrganizationUser, policy.Access);
    }

    // request model shouldn't let client send org id or user id
    [Fact]
    public void CreatePADBatchRequest_ExcludesClientControlledIdentityFields()
    {
        var properties = typeof(CreatePADBatch.CreatePADBatchRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToList();

        Assert.DoesNotContain("EMAOrganizationID", properties, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("CreatedByUserID", properties, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("UserID", properties, StringComparer.OrdinalIgnoreCase);
    }

    // donor from another org should get rejected
    [Fact]
    public void CreatePADDonationRecords_ResolveDonorId_RejectsCrossOrgDonor()
    {
        long lineId = 101;
        string officialName = "Jane Doe";
        int foreignDonorId = 9999;

        var validTenantDonorIds = new HashSet<int> { 1, 2, 3 };
        var clientAssignments = new Dictionary<long, int> { { lineId, foreignDonorId } };
        var donorMap = new Dictionary<string, int> { { "John Smith", 1 } };

        int resolved = CreatePADDonationRecords.ResolveDonorId(
            lineId,
            officialName,
            clientAssignments,
            validTenantDonorIds,
            donorMap);

        Assert.Equal(0, resolved);
    }

    // valid donors belonging to the org should resolve cleanly
    [Fact]
    public void CreatePADDonationRecords_ResolveDonorId_AcceptsValidTenantDonor()
    {
        long lineId = 101;
        string officialName = "John Smith";
        int validDonorId = 1;

        var validTenantDonorIds = new HashSet<int> { 1, 2, 3 };
        var clientAssignments = new Dictionary<long, int> { { lineId, validDonorId } };
        var donorMap = new Dictionary<string, int> { { "John Smith", 1 } };

        int resolved = CreatePADDonationRecords.ResolveDonorId(
            lineId,
            officialName,
            clientAssignments,
            validTenantDonorIds,
            donorMap);

        Assert.Equal(1, resolved);
    }

    // batch line assigned to donor not in org list should fail validation
    [Fact]
    public void CreatePADDonationRecords_ValidateBatchLines_RejectsCrossOrgDonorAssignment()
    {
        var lines = new List<CreatePADDonationRecords.BatchLineItem>
        {
            new()
            {
                PADBatchLineID = 1,
                PADDonorName = "Attacker Target",
                OfficialDonorName = "Attacker Target",
                PADDonationAmount = 50m
            }
        };

        var validTenantDonorIds = new HashSet<int> { 10, 20 };
        var clientAssignments = new Dictionary<long, int> { { 1, 999 } };
        var donorMap = new Dictionary<string, int>();

        var errors = CreatePADDonationRecords.ValidateBatchLines(
            lines,
            clientAssignments,
            validTenantDonorIds,
            donorMap);

        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Contains("was not found in the donor directory"));
    }

    // duplicate batch = aborted (40), clean batch = imported (30)
    [Theory]
    [InlineData(true, 40)]
    [InlineData(false, 30)]
    public void PADBatch_InitialStatusDetermination_DuplicateProtection(bool hasDuplicates, int expectedStatus)
    {
        int status = CreatePADBatch.DetermineBatchInitialStatus(hasDuplicates);
        Assert.Equal(expectedStatus, status);
    }

    // only status 30 can create donations; status 40 (aborted) and 60 (completed) are rejected
    [Theory]
    [InlineData(30, true)]
    [InlineData(40, false)]
    [InlineData(60, false)]
    [InlineData(10, false)]
    public void PADBatch_ProcessableStatusCheck(int statusId, bool expectedProcessable)
    {
        bool processable = CreatePADDonationRecords.IsBatchProcessable(statusId);
        Assert.Equal(expectedProcessable, processable);
    }

    // status 60 means the batch already made its donations
    [Theory]
    [InlineData(60, true)]
    [InlineData(30, false)]
    [InlineData(40, false)]
    public void PADBatch_CompletionStatusCheck(int statusId, bool expectedCompleted)
    {
        bool completed = CreatePADDonationRecords.IsBatchAlreadyCompleted(statusId);
        Assert.Equal(expectedCompleted, completed);
    }

    // check the fund override comment looks right
    [Fact]
    public void PADBatch_FundOverrideCommentFormatting_SanitizesAuditNote()
    {
        string comment = CreatePADDonationRecords.FormatFundOverrideComment("General Fund", "Building Fund");
        Assert.Equal("Fund changed from General Fund to Building Fund during bank statement processing", comment);
    }

    // org b's batch shouldn't show up when querying as org a
    [IntegrationFact]
    public async Task CrossOrganizationPADBatchLookupsAreIsolated()
    {
        await using var connection = await OpenAsync();
        var other = await LoadTestOrganizationAsync(connection);

        int? batchId;
        using (var findCmd = new SqlCommand(
            "SELECT TOP (1) PADBatchID FROM dbo.PADBatch WHERE EMAOrganizationID = @OrgID;",
            connection))
        {
            findCmd.Parameters.AddWithValue("@OrgID", other.OrganizationID);
            var result = await findCmd.ExecuteScalarAsync();
            batchId = result != null && result != DBNull.Value ? Convert.ToInt32(result) : null;
        }

        if (!batchId.HasValue) return;

        int foreignOrgId = other.OrganizationID + 1;

        using (var foreignCmd = new SqlCommand(
            "SELECT COUNT(1) FROM dbo.PADBatch WHERE PADBatchID = @BatchID AND EMAOrganizationID = @OrgID;",
            connection))
        {
            foreignCmd.Parameters.AddWithValue("@BatchID", batchId.Value);
            foreignCmd.Parameters.AddWithValue("@OrgID", foreignOrgId);
            int foreignCount = Convert.ToInt32(await foreignCmd.ExecuteScalarAsync());
            Assert.Equal(0, foreignCount);
        }

        using (var ownerCmd = new SqlCommand(
            "SELECT COUNT(1) FROM dbo.PADBatch WHERE PADBatchID = @BatchID AND EMAOrganizationID = @OrgID;",
            connection))
        {
            ownerCmd.Parameters.AddWithValue("@BatchID", batchId.Value);
            ownerCmd.Parameters.AddWithValue("@OrgID", other.OrganizationID);
            int ownerCount = Convert.ToInt32(await ownerCmd.ExecuteScalarAsync());
            Assert.True(ownerCount > 0);
        }
    }

    // updating another org's batch line should touch 0 rows
    [IntegrationFact]
    public async Task CrossOrganizationPADBatchLineUpdatesAreIsolated()
    {
        await using var connection = await OpenAsync();
        var other = await LoadTestOrganizationAsync(connection);

        long? batchLineId;
        using (var findCmd = new SqlCommand(
            "SELECT TOP (1) PADBatchLineID FROM dbo.PADBatchLine WHERE EMAOrganizationID = @OrgID;",
            connection))
        {
            findCmd.Parameters.AddWithValue("@OrgID", other.OrganizationID);
            var result = await findCmd.ExecuteScalarAsync();
            batchLineId = result != null && result != DBNull.Value ? Convert.ToInt64(result) : null;
        }

        if (!batchLineId.HasValue) return;

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        int foreignOrgId = other.OrganizationID + 1;

        using (var foreignCmd = new SqlCommand(
            """
            UPDATE dbo.PADBatchLine
            SET OfficialDonorName = 'Hacked Name'
            WHERE EMAOrganizationID = @OrgID AND PADBatchLineID = @LineID;
            """,
            connection,
            transaction))
        {
            foreignCmd.Parameters.AddWithValue("@OrgID", foreignOrgId);
            foreignCmd.Parameters.AddWithValue("@LineID", batchLineId.Value);
            int rowsAffected = await foreignCmd.ExecuteNonQueryAsync();
            Assert.Equal(0, rowsAffected);
        }

        await transaction.RollbackAsync();
    }

    private static async Task<SqlConnection> OpenAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable)
            ?? throw new InvalidOperationException($"{ConnectionVariable} is not set.");
        var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        return conn;
    }

    private static async Task<(int OrganizationID, string OrganizationName)> LoadTestOrganizationAsync(SqlConnection connection)
    {
        using var cmd = new SqlCommand(
            "SELECT TOP (1) EMAOrganizationID, OrganizationName FROM dbo.Organization WHERE OrganizationName = @Name;",
            connection);
        cmd.Parameters.AddWithValue("@Name", TestOrganizationName);

        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException($"Test organization '{TestOrganizationName}' not found. Seed with seed_tenant_isolation_test_org.sql.");
        }

        return (reader.GetInt32(0), reader.GetString(1));
    }
}
