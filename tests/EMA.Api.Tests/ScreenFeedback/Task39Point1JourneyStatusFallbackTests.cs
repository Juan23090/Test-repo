namespace EMA.Api.Tests.ScreenFeedback;

public sealed class Task39Point1JourneyStatusFallbackTests
{
    [Fact]
    public void LinkedJourneyUsesValidCurrentOrLatestHistoricalOrganizationStatus()
    {
        var source = File.ReadAllText(
            FindRepositoryFile(
                "src", "api", "EMA.Api", "Services", "ScreenFeedback",
                "SqlScreenFeedbackRepository.cs"));

        Assert.DoesNotContain(
            "ISNULL((SELECT OrganizationStatusID FROM dbo.Organization WHERE EMAOrganizationID = @OrganizationID), 1)",
            source);

        Assert.Contains(
            "DECLARE @JourneyOrganizationStatusID SMALLINT",
            source);

        Assert.Matches(
            @"TRY_CONVERT\s*\(\s*SMALLINT\s*,\s*organization\.OrganizationStatusID\s*\)",
            source);

        Assert.Contains(
            "journey.OrganizationStatusID",
            source);

        Assert.Matches(
            @"ORDER\s+BY\s+journey\.ActivityDateTime\s+DESC\s*,\s*journey\.OrganizationInteractionID\s+DESC",
            source);

        Assert.Contains(
            "THROW 51039, 'The organization does not have a valid status for Journey logging.'",
            source);

        Assert.True(
            CountOccurrences(
                source,
                "@JourneyOrganizationStatusID") >= 6,
            "FAQ, enhancement, and support Journey inserts should all use the resolved status.");
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var start = 0;

        while ((start = source.IndexOf(value, start, StringComparison.Ordinal)) >= 0)
        {
            count++;
            start += value.Length;
        }

        return count;
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(
                new[] { directory.FullName }.Concat(segments).ToArray());

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the EMA repository root.");
    }
}
