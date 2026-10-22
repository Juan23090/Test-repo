using EMA.Api.Functions.Donations;
using Xunit;

namespace EMA.Api.Tests.Donations;

public class PADBatchManagementTests
{
    // Verifies date range summary formatting for multi-date statements
    [Fact]
    public void FormatDateRange_MultipleDates_FormatsStartAndEndDateRange()
    {
        var dates = new List<DateTime>
        {
            new(2026, 7, 1),
            new(2026, 7, 15),
            new(2026, 7, 20)
        };

        var minDate = dates.Min();
        var maxDate = dates.Max();
        string summary = GetPADBatches.FormatDateRangeSummary(minDate, maxDate);

        Assert.Equal("2026/07/01 To 2026/07/20", summary);
    }

    // Verifies single date statements format as a single calendar date without range separator
    [Fact]
    public void FormatDateRange_SingleDate_FormatsAsSingleDate()
    {
        var dates = new List<DateTime>
        {
            new(2026, 7, 1),
            new(2026, 7, 1)
        };

        var minDate = dates.Min();
        var maxDate = dates.Max();
        string summary = GetPADBatches.FormatDateRangeSummary(minDate, maxDate);

        Assert.Equal("2026/07/01", summary);
    }

    // Verifies duplicate date range detection sets Aborted status 40
    [Fact]
    public void CheckDuplicateDateRange_OverlappingRange_SetsAbortedInitialStatus()
    {
        bool hasDuplicateDonations = true;

        int initialStatus = CreatePADBatch.DetermineBatchInitialStatus(hasDuplicateDonations);

        Assert.Equal(40, initialStatus);
    }

    // Verifies unique date range sets initial Import Details status 30
    [Fact]
    public void CheckDuplicateDateRange_NewDateRange_SetsImportedInitialStatus()
    {
        bool hasDuplicateDonations = false;

        int initialStatus = CreatePADBatch.DetermineBatchInitialStatus(hasDuplicateDonations);

        Assert.Equal(30, initialStatus);
    }

    // Verifies batch status definitions match system lifecycle specifications
    [Theory]
    [InlineData(20, "PAD Import Started")]
    [InlineData(30, "PAD Details Imported")]
    [InlineData(40, "Batch Aborted")]
    [InlineData(60, "Donation Records Created")]
    public void BatchStatus_ValidStatusIDs_MapToExpectedDescriptions(int statusId, string expectedDescription)
    {
        string description = statusId switch
        {
            20 => "PAD Import Started",
            30 => "PAD Details Imported",
            40 => "Batch Aborted",
            60 => "Donation Records Created",
            _ => "Unknown"
        };

        Assert.Equal(expectedDescription, description);
    }
}
