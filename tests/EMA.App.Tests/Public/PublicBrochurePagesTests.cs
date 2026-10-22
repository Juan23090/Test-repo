using System.Reflection;
using EMA.App.Components.Pages.Operations;
using EMA.App.Components.Pages.Public;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace EMA.App.Tests.Public;

public sealed class PublicBrochurePagesTests
{
    [Theory]
    [InlineData(typeof(EmaWebsiteDesign), "/emas-design")]
    [InlineData(typeof(OurPartners), "/our-partners")]
    [InlineData(typeof(StatementETransfers), "/stmt-etransfers")]
    [InlineData(typeof(ReferralsPublic), "/referrals-public")]
    [InlineData(typeof(ReferralsPublic), "/referrals")]
    [InlineData(typeof(ReferralsPublic), "/early-adoption-incentive")]
    public void PublicPagesDeclareExpectedRoutes(
        Type pageType,
        string expectedRoute)
    {
        var routes =
            pageType.GetCustomAttributes<RouteAttribute>();

        Assert.Contains(
            routes,
            route => route.Template == expectedRoute);
    }

    [Fact]
    public void PublicLayoutPassesCurrentMenuItemToHeader()
    {
        var repositoryRoot =
            FindRepositoryRoot();

        var source =
            File.ReadAllText(
                Path.Combine(
                    repositoryRoot,
                    "src",
                    "app",
                    "EMA.App",
                    "Layout",
                    "PublicLayout.razor"));

        Assert.Contains(
            "CurrentMenuItem=\"@_currentMenuItem\"",
            source);

        Assert.Contains(
            "CurrentMenuItemChanged=\"HandleCurrentMenuItemChanged\"",
            source);
    }

    [Fact]
    public void PublicLayoutShowsBrochureMenuOnHomePage()
    {
        var repositoryRoot =
            FindRepositoryRoot();

        var source =
            File.ReadAllText(
                Path.Combine(
                    repositoryRoot,
                    "src",
                    "app",
                    "EMA.App",
                    "Layout",
                    "PublicLayout.razor"));

        Assert.DoesNotContain(
            "path is\n                \"\" or",
            source);

        Assert.Contains(
            "return !isLoginRoute;",
            source);
    }

    [Theory]
    [InlineData("emas-website-design.png")]
    [InlineData("statement-etransfer-workflow.png")]
    [InlineData("how-it-works.png")]
    public void PublicWorkflowImagesExist(
        string fileName)
    {
        var repositoryRoot =
            FindRepositoryRoot();

        var imagePath =
            Path.Combine(
                repositoryRoot,
                "src",
                "app",
                "EMA.App",
                "wwwroot",
                "images",
                fileName);

        Assert.True(File.Exists(imagePath));
        Assert.True(new FileInfo(imagePath).Length > 0);
    }

    [Theory]
    [InlineData("microsoft-azure.png")]
    [InlineData("google.png")]
    [InlineData("riipen.png")]
    [InlineData("nait.png")]
    public void PartnerImagesExist(
        string fileName)
    {
        var repositoryRoot =
            FindRepositoryRoot();

        var imagePath =
            Path.Combine(
                repositoryRoot,
                "src",
                "app",
                "EMA.App",
                "wwwroot",
                "images",
                "partners",
                fileName);

        Assert.True(File.Exists(imagePath));
        Assert.True(new FileInfo(imagePath).Length > 0);
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

            if (Directory.Exists(marker))
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
