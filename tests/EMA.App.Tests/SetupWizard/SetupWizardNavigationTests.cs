using Bunit;
using EMA.App.Components;
using EMA.App.Models;
using EMA.App.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace EMA.App.Tests.SetupWizard;

public sealed class SetupWizardNavigationTests : TestContext
{
    public SetupWizardNavigationTests()
    {
        Services.AddSingleton<IMobileMenuStateService, MobileMenuStateService>();
    }

    [Fact]
    public void SetupWizardIsDirectLinkAndMenuRowsMoveToOrganizationMenu()
    {
        var menuNavigation =
            new Mock<IMenuNavigationService>();

        menuNavigation
            .Setup(service =>
                service.GetMenuItemsAsync(
                    18,
                    20,
                    20,
                    false,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new List<MenuItemDto>
                {
                    MenuItem(19000, 1, "Setup Wizard", "Set up Wizard", "M", "/wizard"),
                    MenuItem(19050, 2, "Overview", "About EMA's Design", "M", "/overview"),
                    MenuItem(19500, 2, "Merge", "Merge Data", "B", "/merge-data"),
                    MenuItem(19750, 2, "eTransfers", "How eTransfers Work", "M", "https://example.test/etransfers"),
                    MenuItem(19805, 2, "PADs", "How PADs work", "M", "https://example.test/pads"),
                    MenuItem(20000, 1, "Organization", "Organization", "M", "/organization"),
                    MenuItem(22200, 2, "Savings", "Calculate Your Savings", "M", "/calculate-your-savings")
                });

        menuNavigation
            .Setup(service =>
                service.LogMenuItemUsageAsync(
                    19000,
                    It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Services.AddSingleton(
            menuNavigation.Object);

        var cut =
            RenderComponent<DataDrivenNavigation>(parameters => parameters
                .Add(component => component.EMAOrganizationID, 18)
                .Add(component => component.UserId, 20)
                .Add(component => component.CategoryId, 20));

        var wizardLink =
            cut.FindAll("a.ema-menu-direct-link")
                .Single(link =>
                    link.TextContent.Contains(
                        "Setup Wizard",
                        StringComparison.Ordinal));

        Assert.Equal(
            "/wizard",
            wizardLink.GetAttribute("href"));

        var organizationButton =
            cut.FindAll("button.ema-menu-trigger")
                .Single(button =>
                    button.TextContent.Contains(
                        "Organization",
                        StringComparison.Ordinal));

        var organizationGroupMarkup =
            organizationButton.ParentElement?.OuterHtml
            ?? string.Empty;

        Assert.Contains("About EMA's Design", organizationGroupMarkup);
        Assert.Contains("Merge Data", organizationGroupMarkup);
        Assert.Contains("How eTransfers Work", organizationGroupMarkup);
        Assert.Contains("How PADs work", organizationGroupMarkup);
        Assert.Contains("Calculate Your Savings", organizationGroupMarkup);
        Assert.DoesNotContain(
            cut.FindAll("button.ema-menu-trigger"),
            button =>
                button.TextContent.Contains(
                    "Setup Wizard",
                    StringComparison.Ordinal));

        wizardLink.Click();

        var navigationManager =
            Services.GetRequiredService<NavigationManager>();

        cut.WaitForAssertion(() =>
        {
            Assert.EndsWith(
                "/wizard",
                navigationManager.Uri,
                StringComparison.OrdinalIgnoreCase);
        });

        menuNavigation.Verify(
            service =>
                service.LogMenuItemUsageAsync(
                    19000,
                    It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void DetailItemsBeforeFirstHeaderRemainVisibleUnderThatHeader()
    {
        var menuNavigation = new Mock<IMenuNavigationService>();

        menuNavigation
            .Setup(service => service.GetMenuItemsAsync(
                17, 9, 20, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MenuItemDto>
            {
                MenuItem(19800, 2, "Send Referrals", "Send Referral Invitations", "M", "/send-referrals"),
                MenuItem(19810, 2, "Accept Referral", "Accept a Referral", "M", "/accept-referral"),
                MenuItem(19825, 2, "Referral Details", "Referral Program Summary", "M", "/referral-details"),
                MenuItem(21000, 1, "Organization", "Organization", "M", "/organization"),
                MenuItem(22300, 2, "Mailing Lists", "Mailing Lists", "M", "/mailing-lists-how-it-works"),
                MenuItem(22310, 2, "Groups", "Groups", "M", "/orggroups")
            });

        Services.AddSingleton(menuNavigation.Object);

        var cut = RenderComponent<DataDrivenNavigation>(parameters => parameters
            .Add(component => component.EMAOrganizationID, 17)
            .Add(component => component.UserId, 9)
            .Add(component => component.CategoryId, 20));

        var organizationButton = cut.FindAll("button.ema-menu-trigger")
            .Single(button => button.TextContent.Contains("Organization", StringComparison.Ordinal));

        var organizationGroupMarkup = organizationButton.ParentElement?.OuterHtml ?? string.Empty;
        Assert.Contains("Send Referrals", organizationGroupMarkup);
        Assert.Contains("Accept Referral", organizationGroupMarkup);
        Assert.Contains("Referral Details", organizationGroupMarkup);
        Assert.True(
            organizationGroupMarkup.IndexOf("Mailing Lists", StringComparison.Ordinal) <
            organizationGroupMarkup.IndexOf("Send Referrals", StringComparison.Ordinal));
        Assert.True(
            organizationGroupMarkup.IndexOf("Referral Details", StringComparison.Ordinal) <
            organizationGroupMarkup.IndexOf("Groups", StringComparison.Ordinal));
    }


    [Fact]
    public void WizardOnlyRouteStillResolvesCurrentMenuItemResourcesWithoutJoiningDropdown()
    {
        var menuNavigation =
            new Mock<IMenuNavigationService>();

        var wizardOnlyItem =
            MenuItem(
                19100,
                2,
                "Basic Info",
                "Set Up Organization",
                "W",
                "/basic-information");

        wizardOnlyItem.Resources =
            new List<MenuItemResourceDto>
            {
                new()
                {
                    MenuItemID = 19100,
                    MenuItemResourceTypeID = "V",
                    Sequence = 10,
                    Description = "Setting Up Your Organization",
                    AudioVideoBlob = "video-blob-id"
                },
                new()
                {
                    MenuItemID = 19100,
                    MenuItemResourceTypeID = "A",
                    Sequence = 20,
                    Description = "Organization Lead Through",
                    AudioScript = "Welcome to Email Money App."
                }
            };

        menuNavigation
            .Setup(service =>
                service.GetMenuItemsAsync(
                    18,
                    20,
                    20,
                    false,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new List<MenuItemDto>
                {
                    MenuItem(19070, 1, "Wizard", "Set Up Wizard", "M", "/wizard"),
                    wizardOnlyItem,
                    MenuItem(20000, 1, "Organization", "Organization", "M", "/organization")
                });

        Services.AddSingleton(
            menuNavigation.Object);

        var navigationManager =
            Services.GetRequiredService<NavigationManager>();

        navigationManager.NavigateTo(
            "/basic-information");

        MenuItemDto? resolvedItem = null;

        var cut =
            RenderComponent<DataDrivenNavigation>(parameters => parameters
                .Add(component => component.EMAOrganizationID, 18)
                .Add(component => component.UserId, 20)
                .Add(component => component.CategoryId, 20)
                .Add(
                    component => component.CurrentMenuItemChanged,
                    EventCallback.Factory.Create<MenuItemDto?>(
                        this,
                        item => resolvedItem = item)));

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(resolvedItem);
            Assert.Equal(19100, resolvedItem!.MenuItemID);
            Assert.Equal(2, resolvedItem.Resources.Count);
            Assert.Contains("Set Up Organization", cut.Markup);
        });

        Assert.DoesNotContain(
            cut.FindAll(".ema-menu-dropdown-item"),
            item =>
                item.TextContent.Contains(
                    "Basic Info",
                    StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain(
            cut.Markup,
            "title=\"Set Up Organization\"",
            StringComparison.Ordinal);
    }

    private static MenuItemDto MenuItem(
        int id,
        int row,
        string menuName,
        string screenTitle,
        string displayControlID,
        string url)
    {
        return new MenuItemDto
        {
            MenuItemID = id,
            MenuItemCategoryID = 20,
            Row = row,
            MenuItemTypeID = 11,
            MenuName = menuName,
            ScreenTitle = screenTitle,
            DisplayControlID = displayControlID,
            URL = url,
            IsActive = true
        };
    }
}
