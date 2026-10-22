using Xunit;

namespace EMA.App.Tests.Onboarding;

public sealed class Task26OnboardingSourceTests
{
    [Fact]
    public void ReadyToGetStartedRoutesTheThreeNonSandboxChoicesToPublicRequestForm()
    {
        var source = ReadSource(
            "src", "app", "EMA.App", "Components", "Pages", "Public", "ReadyToGetStarted.razor");

        Assert.Contains("/explore-ema-instantly", source);
        Assert.Contains("/book-a-demo?source=Full%20Data%20Trial&amp;status=30", source);
        Assert.Contains("/book-a-demo?source=Production%20Setup%20on%20Own&amp;status=40", source);
        Assert.Contains("/book-a-demo?source=Production%20Setup%20for%20You&amp;status=50", source);
        Assert.DoesNotContain("GetAuthenticatedDestination", source);
        Assert.DoesNotContain("/dataport?source=Trial", source);
        Assert.DoesNotContain("/dataport?source=Self%20Migrate", source);
    }

    [Fact]
    public void BookADemoUsesTask26WordingAndStatusChoices()
    {
        var source = ReadSource(
            "src", "app", "EMA.App", "Components", "Pages", "Public", "Demo.razor");

        Assert.Contains("@layout PublicLayout", source);
        Assert.Contains("@attribute [AllowAnonymous]", source);
        Assert.Contains("Want to see how we automate eTransfers and Pre-Authorized Debits?", source);
        Assert.Contains("value=\"10\"", source);
        Assert.Contains("value=\"30\"", source);
        Assert.Contains("value=\"40\"", source);
        Assert.Contains("value=\"50\"", source);
        Assert.Contains("Have a demo of your software", source);
        Assert.Contains("Create a demo with realistic, but dummy data", source);
        Assert.Contains("Production setup on Own", source);
        Assert.Contains("Production setup for You", source);
        Assert.Contains("IPublicOnboardingService", source);
    }

    [Fact]
    public void BookADemoIsAllowedByTheCustomRouteGuard()
    {
        var source = ReadSource("src", "app", "EMA.App", "App.razor");

        Assert.Contains("\"/book-a-demo\"", source);

        var authenticatedSessionBlock = source[
            source.IndexOf("private static bool RequiresAuthenticatedSession", StringComparison.Ordinal)..];

        Assert.False(
            authenticatedSessionBlock.Contains(
                "\"/book-a-demo\"",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NewOrganizationEmailVerificationUsesNarrowPublicEndpoints()
    {
        var source = ReadSource(
            "src", "app", "EMA.App", "Services", "EmailVerificationService.cs");

        Assert.Contains("NewOrganizationAdminEmail", source);
        Assert.Contains("/api/public/onboarding/email-verification", source);
        Assert.Contains("GetApiBase", source);
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
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "app", "EMA.App")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the EMA repository root.");
    }
}
