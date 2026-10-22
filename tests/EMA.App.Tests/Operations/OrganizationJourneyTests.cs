using Bunit;
using EMA.App.Components.Pages.EMAInsiders;
using EMA.App.Models;
using EMA.App.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Reflection;
using Xunit;

namespace EMA.App.Tests.Operations;

public class OrganizationJourneyTests : TestContext
{
    private readonly Mock<IOrganizationJourneyService> _mockOrgJourneyService;

    public OrganizationJourneyTests()
    {
        _mockOrgJourneyService = new Mock<IOrganizationJourneyService>();
        _mockOrgJourneyService.Setup(s => s.GetAllOrganizationsAsync())
            .ReturnsAsync(new List<OrganizationSummary>
            {
                new OrganizationSummary { EMAOrganizationID = 18, OrganizationName = "Test Organization" },
                new OrganizationSummary { EMAOrganizationID = 1, OrganizationName = "EMA NonProfit Technologies" }
            });

        _mockOrgJourneyService.Setup(s => s.GetFollowUpQueueAsync())
            .ReturnsAsync(new List<OrganizationFollowUpItem>());

        _mockOrgJourneyService.Setup(s => s.GetJourneyHistoryAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<OrganizationJourneyEntry>());

        _mockOrgJourneyService.Setup(s => s.GetJourneyFeedbackAsync(It.IsAny<int>()))
            .ReturnsAsync(new OrganizationJourneyFeedbackSummary
            {
                Faqs =
                [
                    new OrganizationJourneyFaq
                    {
                        FAQID = 44,
                        OrganizationInteractionID = 901,
                        Question = "How does this work?",
                        Answer = "Like this.",
                        ScreenTitle = "Donations",
                        DateTimeEntered = DateTime.UtcNow
                    }
                ]
            });

        _mockOrgJourneyService.Setup(s => s.GetJourneyActivityTypesAsync())
            .ReturnsAsync(new List<OrganizationJourneyActivityType>());

        _mockOrgJourneyService.Setup(s => s.GetOrganizationStatusesAsync())
            .ReturnsAsync(new List<OrganizationStatusItem>());

        _mockOrgJourneyService.Setup(s => s.GetChMSListAsync())
            .ReturnsAsync(new List<ChMSOption>());

        _mockOrgJourneyService.Setup(s => s.GetAccountingSoftwareAsync())
            .ReturnsAsync(new List<AccountingSoftwareSummary>());

        var mockOrgService = new Mock<IOrganizationService>();
        mockOrgService.Setup(s => s.GetOrganizationAsync(It.IsAny<int>(), default))
            .ReturnsAsync(new OrganizationSummary { EMAOrganizationID = 18, OrganizationName = "Test Organization" });

        var mockOrgState = new Mock<IOrganizationStateService>();
        mockOrgState.Setup(s => s.CurrentOrganization)
            .Returns(new OrganizationSummary { EMAOrganizationID = 18, OrganizationName = "Test Organization" });

        var mockSessionState = new Mock<IUserSessionStateService>();
        mockSessionState.Setup(s => s.CurrentSession)
            .Returns(new UserSession { UserID = 1, EMAOrganizationID = 18, UserName = "Test User" });

        var mockCustomization = new Mock<ICustomizationService>();

        Services.AddSingleton(_mockOrgJourneyService.Object);
        Services.AddSingleton(mockOrgService.Object);
        Services.AddSingleton(mockOrgState.Object);
        Services.AddSingleton(mockSessionState.Object);
        Services.AddSingleton(mockCustomization.Object);
    }

    // Verifies the page route is set to /organization-journey
    [Fact]
    public void OrgJourney_HasCorrectPageRouteAttribute()
    {
        var routeAttr = typeof(OrgJourney).GetCustomAttribute<RouteAttribute>();

        Assert.NotNull(routeAttr);
        Assert.Equal("/organization-journey", routeAttr.Template);
    }

    // Checks that the org journey page renders without errors
    [Fact]
    public void OrgJourneyComponent_RendersContainerAndFormFields()
    {
        var cut = RenderComponent<OrgJourney>();

        Assert.NotNull(cut);
        Assert.Contains("donors-container", cut.Markup);
    }

    // Verifies clicking pick follow up opens the follow up modal
    [Fact]
    public void OrgJourney_ClickingPickFollowUp_OpensFollowUpModal()
    {
        var cut = RenderComponent<OrgJourney>();

        var btn = cut.Find("button.btn-save-donations");
        btn.Click();

        Assert.Contains("Select Follow Up", cut.Markup);
    }

    // Checks that typing in the search box filters the organization list
    [Fact]
    public void OrgJourney_TypingInSearchInput_FiltersOrganizationList()
    {
        var cut = RenderComponent<OrgJourney>();

        var searchInput = cut.Find("input.typeahead");
        searchInput.Input("Test");

        Assert.Contains("Test Organization", cut.Markup);
        Assert.DoesNotContain("EMA NonProfit Technologies", cut.Markup);
    }

    [Fact]
    public async Task OrgJourney_SelectedOrganizationShowsOrganizationNameAndHistoricalFeedbackIcon()
    {
        var cut = RenderComponent<OrgJourney>();
        cut.Find("input.typeahead").Input("Test");
        cut.Find(".typeahead-item").Click();

        await cut.InvokeAsync(() => Task.CompletedTask);
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Organization Name:", cut.Markup);
            Assert.Contains("Test Organization", cut.Markup);
            Assert.Contains("Frequently Asked Questions for this organization", cut.Markup);
            var faqButton = cut.Find("button[aria-label='Frequently Asked Questions for this organization']");
            Assert.Equal("1", faqButton.TextContent.Trim());
            Assert.False(faqButton.HasAttribute("disabled"));
        });
    }
}
