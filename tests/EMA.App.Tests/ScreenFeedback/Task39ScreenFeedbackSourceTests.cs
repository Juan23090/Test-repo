using Xunit;

namespace EMA.App.Tests.ScreenFeedback;

public sealed class Task39ScreenFeedbackSourceTests
{
    [Fact]
    public void HamburgerHasEnterActionsAndClickOutsideDismissLayer()
    {
        var source = ReadSource(
            "src", "app", "EMA.App", "Components", "CategorySwitcher.razor");

        Assert.Contains("Enter", source);
        Assert.Contains("Your Own FAQ", source);
        Assert.Contains("An Enhancement Request", source);
        Assert.Contains("A Support Request", source);
        Assert.Contains("category-switcher-dismiss-layer", source);
        Assert.Contains("@onclick=\"CloseMenu\"", source);
    }

    [Fact]
    public void ScreenActionsAreMountedBesideExistingResourceActions()
    {
        var brochure = ReadSource(
            "src", "app", "EMA.App", "Components", "BrochureHeader.razor");
        var layout = ReadSource(
            "src", "app", "EMA.App", "Layout", "MainLayout.razor");

        Assert.Contains("<MenuItemPageActions", brochure);
        Assert.Contains("<ScreenLevelActions", brochure);
        Assert.Contains("<MenuItemPageActions", layout);
        Assert.Contains("<ScreenLevelActions", layout);
    }

    [Fact]
    public void WorkWithRoutesMatchTask39MenuItems()
    {
        Assert.Contains("@page \"/ww-faqs\"", ReadSource(
            "src", "app", "EMA.App", "Components", "Pages", "EMAInsiders", "WorkWithFaqs.razor"));
        Assert.Contains("@page \"/ww-enhancements\"", ReadSource(
            "src", "app", "EMA.App", "Components", "Pages", "EMAInsiders", "WorkWithEnhancements.razor"));
        Assert.Contains("@page \"/ww-tickets\"", ReadSource(
            "src", "app", "EMA.App", "Components", "Pages", "EMAInsiders", "WorkWithSupportTickets.razor"));
    }

    [Fact]
    public void PublicFaqPageIsAnonymousAndUsesPublishedSnapshot()
    {
        var page = ReadSource(
            "src", "app", "EMA.App", "Components", "Pages", "Public", "PublicFaqs.razor");
        var app = ReadSource("src", "app", "EMA.App", "App.razor");

        Assert.Contains("@page \"/faqs\"", page);
        Assert.Contains("@attribute [AllowAnonymous]", page);
        Assert.Contains("GetPublishedFaqHtmlAsync", page);
        Assert.Contains("No public FAQs are currently available.", page);
        Assert.DoesNotContain("GetPublicFaqsAsync", page);
        Assert.Contains("\"/faqs\"", app);
    }

    [Fact]
    public void FaqPlaceholderUsesPlainApostrophe()
    {
        var source = ReadSource(
            "src", "app", "EMA.App", "Components", "ScreenLevelActions.razor");

        Assert.Contains("if you don't know", source);
        Assert.DoesNotContain("donâ€™t", source);
    }

    [Fact]
    public void AppCssDoesNotContainKnownEncodingCorruptionMarkers()
    {
        var source = ReadSource(
            "src", "app", "EMA.App", "wwwroot", "css", "app.css");

        Assert.DoesNotContain("Ã", source);
        Assert.DoesNotContain("â", source);
    }

    [Fact]
    public void Task39TimestampsUseOrganizationDateFormatAfterLocalConversion()
    {
        var files = new[]
        {
            ReadSource("src", "app", "EMA.App", "Components", "ScreenLevelActions.razor"),
            ReadSource("src", "app", "EMA.App", "Components", "Pages", "EMAInsiders", "WorkWithFaqs.razor"),
            ReadSource("src", "app", "EMA.App", "Components", "Pages", "EMAInsiders", "WorkWithEnhancements.razor"),
            ReadSource("src", "app", "EMA.App", "Components", "Pages", "EMAInsiders", "WorkWithSupportTickets.razor")
        };

        foreach (var source in files)
        {
            Assert.Contains("OrgDateFormat.Format", source);
            Assert.Contains(".ToLocalTime()", source);
            Assert.DoesNotContain("ToString(\"yyyy/MM/dd HH:mm\")", source);
        }
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
