using Xunit;

namespace EMA.App.Tests.Navigation;

public sealed class Task40Point2RogerUiTweaksSourceTests
{
    [Fact]
    public void JourneyFeedbackIconsRenderOnlyWhenCountIsPositive()
    {
        var source = ReadSource(
            "src", "app", "EMA.App", "Components", "Pages", "EMAInsiders", "OrgJourney.razor");

        Assert.Contains("@if (JourneyFeedback.Faqs.Count > 0)", source);
        Assert.Contains("@if (JourneyFeedback.Enhancements.Count > 0)", source);
        Assert.Contains("@if (JourneyFeedback.SupportTickets.Count > 0)", source);
        Assert.DoesNotContain("disabled=\"@(JourneyFeedback.Faqs.Count == 0)\"", source);
        Assert.DoesNotContain("disabled=\"@(JourneyFeedback.Enhancements.Count == 0)\"", source);
        Assert.DoesNotContain("disabled=\"@(JourneyFeedback.SupportTickets.Count == 0)\"", source);
    }

    [Fact]
    public void MobileShowMenuIsRaisedSlightly()
    {
        var source = ReadSource(
            "src", "app", "EMA.App", "Layout", "MainLayout.razor.css");

        Assert.Contains(".app-header-logo-mobile", source);
        Assert.Contains("transform: translateY(-0.35rem);", source);
    }

    [Fact]
    public void Task40Point2TemporaryLandingRoutesExist()
    {
        Assert.Contains("@page \"/pub20260906\"", ReadSource(
            "src", "app", "EMA.App", "Components", "Pages", "Landing", "PublicLanding20260906.razor"));
        Assert.Contains("@page \"/org20260906\"", ReadSource(
            "src", "app", "EMA.App", "Components", "Pages", "Landing", "OrganizationLanding20260906.razor"));
        Assert.Contains("@page \"/don20260906\"", ReadSource(
            "src", "app", "EMA.App", "Components", "Pages", "Landing", "DonorLanding20260906.razor"));
        Assert.Contains("@page \"/ins20260906\"", ReadSource(
            "src", "app", "EMA.App", "Components", "Pages", "Landing", "InsiderLanding20260906.razor"));
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
