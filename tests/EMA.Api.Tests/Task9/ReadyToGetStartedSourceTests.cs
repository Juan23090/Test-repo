namespace EMA.Api.Tests.Task9;

public sealed class ReadyToGetStartedSourceTests
{
    [Fact]
    public void PageContainsRequiredPublicOnboardingRoutesAndNoSmsConsent()
    {
        var repositoryRoot =
            FindRepositoryRoot();

        var pagePath =
            Path.Combine(
                repositoryRoot,
                "src",
                "app",
                "EMA.App",
                "Components",
                "Pages",
                "Public",
                "ReadyToGetStarted.razor");

        var source =
            File.ReadAllText(
                pagePath);

        Assert.Contains(
            "@page \"/ready-to-get-started\"",
            source);

        Assert.Contains(
            "/explore-ema-instantly",
            source);

        Assert.Contains(
            "/book-a-demo?source=Full%20Data%20Trial&amp;status=30",
            source);

        Assert.Contains(
            "/book-a-demo?source=Production%20Setup%20on%20Own&amp;status=40",
            source);

        Assert.Contains(
            "/book-a-demo?source=Production%20Setup%20for%20You&amp;status=50",
            source);

        Assert.DoesNotContain(
            "GetAuthenticatedDestination",
            source);

        Assert.False(
            source.Contains(
                "SMS",
                StringComparison.OrdinalIgnoreCase));
    }

    private static string FindRepositoryRoot()
    {
        var directory =
            new DirectoryInfo(
                AppContext.BaseDirectory);

        while (directory is not null)
        {
            var marker =
                Path.Combine(
                    directory.FullName,
                    "src",
                    "app",
                    "EMA.App");

            if (Directory.Exists(
                    marker))
            {
                return directory.FullName;
            }

            directory =
                directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the EMA repository root.");
    }
}
