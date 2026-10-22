using Bunit;
using EMA.App.Components.Pages.Operations;
using EMA.App.Models;
using EMA.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace EMA.App.Tests.SetupWizard;

public sealed class SetupWizardTests : TestContext
{
    [Fact]
    public void OrganizationContextShowsWizardStepsWithoutOrganizationHeader()
    {
        var sessionState =
            SessionState(
                currentCategoryID:
                    MenuNavigationConstants.OperationsCategoryID);

        var wizardService =
            new Mock<ISetupWizardService>();

        wizardService
            .Setup(service =>
                service.GetForOrganizationUserAsync(
                    18,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                Wizard(
                    18,
                    "Test Organization"));

        RegisterServices(
            sessionState,
            wizardService);

        var cut =
            RenderComponent<EMA.App.Components.Pages.Operations.SetupWizard>();

        Assert.DoesNotContain(
            "setup-wizard-organization-name",
            cut.Markup);
        Assert.DoesNotContain(
            "setup-wizard-selected-organization",
            cut.Markup);
        Assert.Contains(
            "Set up Organization",
            cut.Markup);
        Assert.Contains(
            "3",
            cut.Markup);
        Assert.DoesNotContain(
            "setup-wizard-organization-search",
            cut.Markup);
    }

    [Fact]
    public void InsiderContextSearchesOrganizationsAndLoadsSelectedProgress()
    {
        var sessionState =
            SessionState(
                currentCategoryID:
                    MenuNavigationConstants.SupportCategoryID);

        var wizardService =
            new Mock<ISetupWizardService>();

        wizardService
            .Setup(service =>
                service.GetForInsiderAsync(
                    18,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                Wizard(
                    18,
                    "Test Organization"));

        var organizations =
            new Mock<IOrganizationJourneyService>();

        organizations
            .Setup(service =>
                service.GetAllOrganizationsAsync())
            .ReturnsAsync(
                new List<OrganizationSummary>
                {
                    new()
                    {
                        EMAOrganizationID = 18,
                        OrganizationName = "Test Organization"
                    },
                    new()
                    {
                        EMAOrganizationID = 44,
                        OrganizationName = "Another Charity"
                    }
                });

        RegisterServices(
            sessionState,
            wizardService,
            organizations);

        var cut =
            RenderComponent<EMA.App.Components.Pages.Operations.SetupWizard>();

        var input =
            cut.Find(
                "input.setup-wizard-organization-search");

        input.Input("Test");

        Assert.Contains(
            "Test Organization",
            cut.Markup);
        Assert.DoesNotContain(
            "Another Charity",
            cut.Markup);

        cut.Find(
                "button.setup-wizard-organization-result")
            .Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(
                "Set up Organization",
                cut.Markup);

            wizardService.Verify(
                service =>
                    service.GetForInsiderAsync(
                        18,
                        It.IsAny<CancellationToken>()),
                Times.Once);
        });
    }

    [Fact]
    public void StepClickLogsMenuItemUsageAndNavigates()
    {
        var sessionState =
            SessionState(
                currentCategoryID:
                    MenuNavigationConstants.OperationsCategoryID);

        var wizardService =
            new Mock<ISetupWizardService>();

        wizardService
            .Setup(service =>
                service.GetForOrganizationUserAsync(
                    18,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                Wizard(
                    18,
                    "Test Organization"));

        var navigation =
            new Mock<IMenuNavigationService>();

        navigation
            .Setup(service =>
                service.LogMenuItemUsageAsync(
                    19100,
                    It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        RegisterServices(
            sessionState,
            wizardService,
            menuNavigationService: navigation);

        var cut =
            RenderComponent<EMA.App.Components.Pages.Operations.SetupWizard>();

        cut.Find(
                "button.setup-wizard-step-link")
            .Click();

        navigation.Verify(
            service =>
                service.LogMenuItemUsageAsync(
                    19100,
                    It.IsAny<CancellationToken>()),
            Times.Once);

        var navManager =
            Services.GetRequiredService<
                Microsoft.AspNetCore.Components.NavigationManager>();
        Assert.EndsWith("/basic-information", navManager.Uri, StringComparison.OrdinalIgnoreCase);
    }

    private void RegisterServices(
        Mock<IUserSessionStateService> sessionState,
        Mock<ISetupWizardService> wizardService,
        Mock<IOrganizationJourneyService>? organizationJourneyService = null,
        Mock<IMenuNavigationService>? menuNavigationService = null)
    {
        if (organizationJourneyService is null)
        {
            organizationJourneyService =
                new Mock<IOrganizationJourneyService>();

            organizationJourneyService
                .Setup(service =>
                    service.GetAllOrganizationsAsync())
                .ReturnsAsync(
                    new List<OrganizationSummary>());
        }

        if (menuNavigationService is null)
        {
            menuNavigationService =
                new Mock<IMenuNavigationService>();

            menuNavigationService
                .Setup(service =>
                    service.LogMenuItemUsageAsync(
                        It.IsAny<int>(),
                        It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }

        Services.AddSingleton(
            sessionState.Object);
        Services.AddSingleton(
            wizardService.Object);
        Services.AddSingleton(
            organizationJourneyService.Object);
        Services.AddSingleton(
            menuNavigationService.Object);
    }

    private static Mock<IUserSessionStateService> SessionState(
        int currentCategoryID)
    {
        var sessionState =
            new Mock<IUserSessionStateService>();

        sessionState
            .Setup(service =>
                service.InitializeAsync())
            .Returns(Task.CompletedTask);

        sessionState
            .Setup(service =>
                service.CurrentSession)
            .Returns(
                new UserSession
                {
                    UserID = 7,
                    EMAOrganizationID = 18,
                    OrganizationName = "Test Organization",
                    CurrentCategoryID = currentCategoryID,
                    AllowedCategoryIDs =
                    [
                        MenuNavigationConstants.OperationsCategoryID,
                        MenuNavigationConstants.SupportCategoryID
                    ],
                    AccessToken = "test-token",
                    AccessTokenExpiresAtUtc =
                        DateTimeOffset.UtcNow.AddHours(1)
                });

        return sessionState;
    }

    private static SetupWizardResult Wizard(
        int organizationID,
        string organizationName)
    {
        return new SetupWizardResult
        {
            EMAOrganizationID = organizationID,
            OrganizationName = organizationName,
            Steps =
            [
                new SetupWizardStep
                {
                    MenuItemID = 19100,
                    ScreenTitle = "Set up Organization",
                    Url = "/basic-information",
                    VisitCount = 3,
                    LastVisitUtc =
                        new DateTime(
                            2026,
                            8,
                            16,
                            18,
                            0,
                            0,
                            DateTimeKind.Utc)
                }
            ]
        };
    }
}
