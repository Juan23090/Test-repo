using EMA.App.Services;
using Xunit;

namespace EMA.App.Tests.Navigation;

public sealed class Task40NavigationBrandingTests
{
    [Fact]
    public void MobileMenuStateCanExpandCollapseAndToggle()
    {
        var state = new MobileMenuStateService();
        var notifications = 0;
        state.OnChange += () => notifications++;

        Assert.False(state.IsExpanded);

        state.Expand();
        Assert.True(state.IsExpanded);

        state.Toggle();
        Assert.False(state.IsExpanded);

        state.Collapse();
        Assert.False(state.IsExpanded);

        Assert.Equal(2, notifications);
    }

    [Fact]
    public void TimeoutFlowRedirectsToLoginWithFriendlyMessage()
    {
        var handler = ReadSource(
            "src", "app", "EMA.App", "Services", "ApiAccessTokenHandler.cs");
        var app = ReadSource(
            "src", "app", "EMA.App", "App.razor");
        var login = ReadSource(
            "src", "app", "EMA.App", "Components", "Pages", "Layout", "Login.razor");

        Assert.Contains("sessionExpired: true", handler);
        Assert.Contains("BuildSessionExpiredLoginUrl", app);
        Assert.Contains("Your session has timed out. Please log in again.", login);
    }

    [Fact]
    public void HamburgerStillDismissesWhenClickingOutside()
    {
        var source = ReadSource(
            "src", "app", "EMA.App", "Components", "CategorySwitcher.razor");

        Assert.Contains("category-switcher-dismiss-layer", source);
        Assert.Contains("@onclick=\"CloseMenu\"", source);
    }

    [Fact]
    public void BrandingUsesEmaGivingAndInsiderName()
    {
        var branding = ReadSource(
            "src", "app", "EMA.App", "Models", "AppBranding.cs");
        var layout = ReadSource(
            "src", "app", "EMA.App", "Layout", "MainLayout.razor");

        Assert.Contains("EMA Giving", branding);
        Assert.Contains("ema.giving", branding);
        Assert.Contains("EMA Insiders", branding);
        Assert.Contains("MenuNavigationConstants.SupportCategoryID", layout);
        Assert.Contains("AppBranding.InsiderName", layout);
    }

    [Fact]
    public void MobileNavigationUsesSharedShowMenuState()
    {
        var navigation = ReadSource(
            "src", "app", "EMA.App", "Components", "DataDrivenNavigation.razor");
        var layout = ReadSource(
            "src", "app", "EMA.App", "Layout", "MainLayout.razor");
        var brochure = ReadSource(
            "src", "app", "EMA.App", "Components", "BrochureHeader.razor");
        var program = ReadSource(
            "src", "app", "EMA.App", "Program.cs");

        Assert.Contains("IMobileMenuStateService", navigation);
        Assert.Contains("MobileMenuState.Collapse()", navigation);
        Assert.Contains("Show Menu", layout);
        Assert.Contains("Show Menu", brochure);
        Assert.Contains("MobileMenuStateService", program);
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
