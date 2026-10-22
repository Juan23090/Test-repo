namespace EMA.Api.Tests.SetupWizard;

public sealed class SetupWizardSourceContractTests
{
    [Fact]
    public void SqlQueryUsesWizardDisplayControlsAndTenantScopedUsage()
    {
        var source =
            File.ReadAllText(
                FindRepositoryFile(
                    "src",
                    "api",
                    "EMA.Api",
                    "Services",
                    "Navigation",
                    "SqlSetupWizardQuery.cs"));

        Assert.Contains(
            "IN ('W', 'B')",
            source);
        Assert.Contains(
            "dbo.MenuItemUsage",
            source);
        Assert.Contains(
            "dbo.ApiSession",
            source);
        Assert.Contains(
            "dbo.AuthenticationSession",
            source);
        Assert.Contains(
            "dbo.LoginInstance",
            source);
        Assert.Contains(
            "apiSession.EMAOrganizationID",
            source);
        Assert.Contains(
            "authSession.EMAOrganizationID",
            source);
        Assert.Contains(
            "loginOrganizationUser.EMAOrganizationID",
            source);
        Assert.Contains(
            "dbo.OrganizationUserRole",
            source);
        Assert.Contains(
            "dbo.RoleMenuItem",
            source);
    }

    [Fact]
    public void NavigationDataIncludesWizardRowsWhileVisibleMenuStillFiltersThemOut()
    {
        var apiSource =
            File.ReadAllText(
                FindRepositoryFile(
                    "src",
                    "api",
                    "EMA.Api",
                    "Functions",
                    "Roles",
                    "GetMenuItemsByUser.cs"));

        var navigationSource =
            File.ReadAllText(
                FindRepositoryFile(
                    "src",
                    "app",
                    "EMA.App",
                    "Components",
                    "DataDrivenNavigation.razor"));

        Assert.Contains(
            "IN ('M', 'B', 'W')",
            apiSource);
        Assert.Contains(
            "MenuItemResourceQuery.LoadAsync",
            apiSource);
        Assert.Contains(
            "_availableMenuItems",
            navigationSource);
        Assert.Contains(
            "return displayControlID is \"M\" or \"B\";",
            navigationSource);
    }


    [Fact]
    public void LandingDisplayControlIsMaintainedAndDrivesLandingRoutes()
    {
        var menuMaintenance =
            File.ReadAllText(
                FindRepositoryFile(
                    "src",
                    "app",
                    "EMA.App",
                    "Components",
                    "Pages",
                    "EMAInsiders",
                    "MenuAndRoles.razor"));

        var routeAccess =
            File.ReadAllText(
                FindRepositoryFile(
                    "src",
                    "api",
                    "EMA.Api",
                    "Functions",
                    "Navigation",
                    "GetRouteAccess.cs"));

        var landingPage =
            File.ReadAllText(
                FindRepositoryFile(
                    "src",
                    "api",
                    "EMA.Api",
                    "Functions",
                    "Navigation",
                    "GetCategoryLandingPage.cs"));

        Assert.Contains(
            "<option value=\"L\">Landing Page</option>",
            menuMaintenance);
        Assert.Contains(
            "= 'L'",
            routeAccess);
        Assert.Contains(
            "= 'L'",
            landingPage);
        Assert.DoesNotContain(
            "IN ('M', 'B')",
            landingPage);
        Assert.DoesNotContain(
            "mi.MenuItemTypeID = 11",
            landingPage);
    }

    private static string FindRepositoryFile(
        params string[] segments)
    {
        var directory =
            new DirectoryInfo(
                AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate =
                Path.Combine(
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
