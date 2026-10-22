namespace EMA.Api.Tests.Navigation;

public sealed class Task40Point2LandingPageSourceContractTests
{
    [Fact]
    public void LandingLookupsUseDisplayControlLAsTheSourceOfTruth()
    {
        foreach (var relativePath in new[]
        {
            new[] { "src", "api", "EMA.Api", "Functions", "Navigation", "GetCategoryLandingPage.cs" },
            new[] { "src", "api", "EMA.Api", "Functions", "Navigation", "GetDonorCategoryLandingPage.cs" }
        })
        {
            var source = File.ReadAllText(FindRepositoryFile(relativePath));

            Assert.Contains("= 'L'", source);
            Assert.DoesNotContain("mi.MenuItemTypeID = 11", source);
            Assert.DoesNotContain("IN ('M', 'B')", source);
            Assert.Contains("IsConfiguredLandingPage =", source);
            Assert.Contains("true", source);
        }
    }

    [Fact]
    public void RouteAccessUsesDisplayControlLInsteadOfLegacyCategoryPointer()
    {
        foreach (var relativePath in new[]
        {
            new[] { "src", "api", "EMA.Api", "Functions", "Navigation", "GetRouteAccess.cs" },
            new[] { "src", "api", "EMA.Api", "Functions", "Navigation", "GetDonorRouteAccess.cs" }
        })
        {
            var source = File.ReadAllText(FindRepositoryFile(relativePath));

            Assert.Contains("DisplayControlID", source);
            Assert.Contains("= 'L'", source);
            Assert.DoesNotContain("mic.LandingPageMenuItemID = mi.MenuItemID", source);
        }
    }

    [Fact]
    public void MenuMaintenanceSupportsDedicatedLandingRowAndTypeZero()
    {
        var apiSource = File.ReadAllText(
            FindRepositoryFile(
                "src", "api", "EMA.Api", "Functions", "Roles", "SaveMenuItem.cs"));

        var appSource = File.ReadAllText(
            FindRepositoryFile(
                "src", "app", "EMA.App", "Components", "Pages", "EMAInsiders", "MenuAndRoles.razor"));

        Assert.Contains("HasValidLandingCoordinates", apiSource);
        Assert.Contains("row == 0", apiSource);
        Assert.Contains("menuItemTypeID == 0", apiSource);
        Assert.Contains("normalizedDisplayControlID != \"L\"", apiSource);
        Assert.Contains("HasValidLandingCoordinates", appSource);
        Assert.Contains("Landing Pages must use Row 0", appSource);
        Assert.Contains("!IsLandingMenuItem(CurrentMenuItem)", appSource);
        Assert.Contains("<option value=\"-1\">-- Select --</option>", appSource);
    }

    [Fact]
    public void TemporaryLandingPagesUseTheVersionedTask40Point2Routes()
    {
        var expected = new Dictionary<string, string>
        {
            ["PublicLanding20260906.razor"] = "@page \"/pub20260906\"",
            ["OrganizationLanding20260906.razor"] = "@page \"/org20260906\"",
            ["DonorLanding20260906.razor"] = "@page \"/don20260906\"",
            ["InsiderLanding20260906.razor"] = "@page \"/ins20260906\""
        };

        foreach (var item in expected)
        {
            var source = File.ReadAllText(
                FindRepositoryFile(
                    "src", "app", "EMA.App", "Components", "Pages", "Landing", item.Key));

            Assert.Contains(item.Value, source);
            Assert.Contains("Temporary Landing Page", source);
        }
    }

    [Fact]
    public void LandingItemsRemainHiddenFromVisibleMenus()
    {
        var publicMenuSource = File.ReadAllText(
            FindRepositoryFile(
                "src", "api", "EMA.Api", "Functions", "Navigation", "GetPublicBrochureMenuItems.cs"));

        var userNavigationSource = File.ReadAllText(
            FindRepositoryFile(
                "src", "app", "EMA.App", "Components", "DataDrivenNavigation.razor"));

        Assert.Contains("IN ('M', 'B')", publicMenuSource);
        Assert.Contains("return displayControlID is \"M\" or \"B\";", userNavigationSource);
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
