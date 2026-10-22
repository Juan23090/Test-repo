using EMA.Api.Functions.Donations;
using Xunit;

namespace EMA.Api.Tests.Donations;

public class CreatePADDonationRecordsTests
{
    // Verifies batch validation collects error messages for all unmatched lines
    [Fact]
    public void ValidateBatchLines_UnmatchedLines_AggregatesAllErrorMessages()
    {
        var lines = new List<CreatePADDonationRecords.BatchLineItem>
        {
            new() { PADBatchLineID = 1, PADDonorName = "Christian Bale", OfficialDonorName = "Christian Bale" },
            new() { PADBatchLineID = 2, PADDonorName = "Matt Damon", OfficialDonorName = "" },
            new() { PADBatchLineID = 3, PADDonorName = "Jennifer Lawrence", OfficialDonorName = "" }
        };

        var donorMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "Christian Bale", 101 }
        };
        var validDonorIds = new HashSet<int> { 101 };

        var validationErrors = CreatePADDonationRecords.ValidateBatchLines(lines, null, validDonorIds, donorMap);

        Assert.Equal(2, validationErrors.Count);
        Assert.Contains("Line 2", validationErrors[0]);
        Assert.Contains("Line 3", validationErrors[1]);
    }

    // Verifies batch status 60 triggers idempotency guard to prevent duplicate donation rows
    [Theory]
    [InlineData(60, true)]
    [InlineData(30, false)]
    [InlineData(20, false)]
    public void CheckBatchStatus_IdempotencyGuard_RejectsAlreadyCreatedBatches(int statusId, bool expectedRejection)
    {
        bool isAlreadyCreated = CreatePADDonationRecords.IsBatchAlreadyCompleted(statusId);

        Assert.Equal(expectedRejection, isAlreadyCreated);
    }

    // Verifies explicit client donor ID assignments take precedence over string name matching
    [Fact]
    public void ResolveDonorId_ExplicitClientAssignment_PrefersExactAssignedId()
    {
        long lineId = 10;
        string officialName = "Jennifer Lawrence";
        var clientAssignments = new Dictionary<long, int> { { 10, 2001 } };
        var validDonorIds = new HashSet<int> { 2001, 2002 };
        var nameToIdMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { { "Jennifer Lawrence", 2002 } };

        int resolvedId = CreatePADDonationRecords.ResolveDonorId(lineId, officialName, clientAssignments, validDonorIds, nameToIdMap);

        Assert.Equal(2001, resolvedId);
    }

    // Verifies fallback to name dictionary when no client assignment exists
    [Fact]
    public void ResolveDonorId_NoClientAssignment_FallsBackToVerifiedNameMap()
    {
        long lineId = 12;
        string officialName = "Christian Bale & Matt Damon";
        var clientAssignments = new Dictionary<long, int>();
        var validDonorIds = new HashSet<int> { 5001 };
        var nameToIdMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { { "Christian Bale & Matt Damon", 5001 } };

        int resolvedId = CreatePADDonationRecords.ResolveDonorId(lineId, officialName, clientAssignments, validDonorIds, nameToIdMap);

        Assert.Equal(5001, resolvedId);
    }

    // Verifies lines marked in DeletedLineIDs are bypassed during validation
    [Fact]
    public void ValidateBatchLines_DeletedLines_ExcludesFromValidationErrors()
    {
        var lines = new List<CreatePADDonationRecords.BatchLineItem>
        {
            new() { PADBatchLineID = 1, PADDonorName = "Christian Bale", OfficialDonorName = "Christian Bale" },
            new() { PADBatchLineID = 2, PADDonorName = "MONTHLY ACCOUNT FEE", OfficialDonorName = "" }, // Unmatched fee row
            new() { PADBatchLineID = 3, PADDonorName = "Matt Damon", OfficialDonorName = "" }
        };

        var donorMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "Christian Bale", 101 }
        };
        var validDonorIds = new HashSet<int> { 101 };
        var deletedLineIds = new HashSet<long> { 2 }; // Line 2 marked as deleted

        var validationErrors = CreatePADDonationRecords.ValidateBatchLines(lines, null, validDonorIds, donorMap, deletedLineIds);

        // Only line 3 should error, line 2 should be skipped because it's deleted
        Assert.Single(validationErrors);
        Assert.Contains("Line 3", validationErrors[0]);
    }

    // Verifies fund override comment string matches stored procedure parsing contract
    [Fact]
    public void FormatFundOverrideComment_OverriddenFund_ProducesExpectedAuditString()
    {
        string defaultFundName = "General Donations";
        string overriddenFundName = "Student Aid";

        string comment = CreatePADDonationRecords.FormatFundOverrideComment(defaultFundName, overriddenFundName);

        Assert.Equal("Fund changed from General Donations to Student Aid during bank statement processing", comment);
        Assert.Contains("Fund changed from", comment);
        Assert.Contains(" to ", comment);
        Assert.Contains(" during bank statement processing", comment);
    }
}
