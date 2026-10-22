namespace EMA.Api.Tests.ScreenFeedback;

public sealed class Task41PublicFaqMigrationTests
{
    [Fact]
    public void MigrationAddsCopyToPublicAndSupportsGlobalFaqsSafely()
    {
        var source = ReadSource(
            "src", "db", "migrations", "20260920_Task41PublicFaqPublishing.sql");

        Assert.Contains("CopyToPublic CHAR(1) NOT NULL", source);
        Assert.Contains("DEFAULT ('N')", source);
        Assert.Contains("ALTER COLUMN MenuItemID INT NULL", source);
        Assert.Contains("CK_FrequentlyAskedQuestions_CopyToPublic", source);
        Assert.Contains("CopyToPublic must default to N", source);
        Assert.Contains("trusted Y/N CopyToPublic check constraint", source);
        Assert.Contains("LOWER(LTRIM(RTRIM(URL))) = '/ww-faqs'", source);
        Assert.Contains("SET XACT_ABORT ON", source);
        Assert.Contains("BEGIN TRANSACTION", source);
        Assert.Contains("ROLLBACK TRANSACTION", source);
        Assert.Contains("unexpected type", source);
    }

    [Fact]
    public void VerifierIsReadOnlyAndChecksTask41Contract()
    {
        var source = ReadSource(
            "src", "db", "VerifyTask41PublicFaqPublishing.sql");
        var upper = source.ToUpperInvariant();

        Assert.Contains("COPYTOPUBLIC", upper);
        Assert.Contains("MENUITEMID", upper);
        Assert.Contains("IS_NULLABLE = 1", upper);
        Assert.DoesNotContain("INSERT INTO", upper);
        Assert.DoesNotContain("UPDATE DBO", upper);
        Assert.DoesNotContain("DELETE FROM", upper);
        Assert.DoesNotContain("ALTER TABLE", upper);
        Assert.DoesNotContain("CREATE TABLE", upper);
        Assert.DoesNotContain("DROP TABLE", upper);
    }

    [Fact]
    public void RepositoryPublishesOnlyReviewedActiveExplicitlyPublicFaqs()
    {
        var source = ReadSource(
            "src", "api", "EMA.Api", "Services", "ScreenFeedback",
            "SqlScreenFeedbackRepository.cs");

        Assert.Contains("faq.CopyToPublic", source);
        Assert.Contains("UPPER(LTRIM(RTRIM(faq.CopyToPublic))) = 'Y'", source);
        Assert.Contains("faq.MenuItemID IS NULL OR menuItem.IsActive = 'Y'", source);
        Assert.Contains("LEFT JOIN dbo.MenuItem AS menuItem", source);
        Assert.Contains("CreateGlobalFaqAsync", source);
        Assert.Contains("@JourneyMenuItemID", source);
        Assert.Contains("@JourneyMenuItemCount <> 1", source);
    }

    private static string ReadSource(params string[] segments)
    {
        var path = segments.Aggregate(FindRepositoryRoot(), Path.Combine);
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "api", "EMA.Api")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the EMA repository root.");
    }
}
