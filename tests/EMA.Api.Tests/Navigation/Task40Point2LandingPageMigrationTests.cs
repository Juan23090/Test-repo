namespace EMA.Api.Tests.Navigation;

public sealed class Task40Point2LandingPageMigrationTests
{
    [Fact]
    public void DeploymentMigrationIsIdempotentGuardedAndContainsTheTask40Point2Contract()
    {
        var source = File.ReadAllText(
            FindRepositoryFile(
                "src", "db", "migrations",
                "20260915_ProvisionTask40Point2LandingPages.sql"));

        Assert.Contains("SET XACT_ABORT ON", source);
        Assert.Contains("BEGIN TRANSACTION", source);
        Assert.Contains("ROLLBACK TRANSACTION", source);
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM dbo.MenuItemRow WHERE MenuItemRowID = 0)", source);
        Assert.Contains("VALUES (0, N'Landing Page')", source);
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM dbo.MenuItemType WHERE MenuItemTypeID = 0)", source);
        Assert.Contains("VALUES (0, N'Landing')", source);

        Assert.Contains("(10000, 10, '/pub20260906'", source);
        Assert.Contains("(20000, 20, '/org20260906'", source);
        Assert.Contains("(30000, 30, '/don20260906'", source);
        Assert.Contains("(90000, 90, '/ins20260906'", source);
        Assert.Contains("actual.MenuItemRowID = 0", source);
        Assert.Contains("actual.MenuItemTypeID = 0", source);
        Assert.Contains("actual.DisplayControlID = 'L'", source);
        Assert.Contains("actual.IsActive = 'Y'", source);

        Assert.Contains("A Task 40.2 MenuItemID already belongs to a different URL", source);
        Assert.Contains("A Task 40.2 landing URL already belongs to a different MenuItemID", source);
        Assert.Contains("An unknown active landing MenuItem already exists", source);

        Assert.Contains("(10,   20000)", source);
        Assert.Contains("(20,   20000)", source);
        Assert.Contains("(3000, 30000)", source);
        Assert.Contains("(3010, 30000)", source);
        Assert.Contains("(9000, 90000)", source);
        Assert.DoesNotContain("(9000, 10000)", source);

        Assert.Contains("(21010, 20, '/kpis')", source);
        Assert.Contains("(30110, 30, '/donor-dashboard')", source);
        Assert.Contains("(90050, 90, '/ema-insiders')", source);

        Assert.DoesNotContain("LandingPageMenuItemID =", source);
        Assert.Contains("VerifyTask40Point2LandingPages.sql", source);
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(
                new[] { directory.FullName }
                    .Concat(segments)
                    .ToArray());

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
