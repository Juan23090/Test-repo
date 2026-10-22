using Bunit;
using EMA.App.Components.Pages.Operations;
using EMA.App.Models;
using EMA.App.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Moq;
using System.Reflection;
using Xunit;

namespace EMA.App.Tests.Donors;

public class DonorJourneyTests : TestContext
{
    private readonly Mock<IDonorService> _mockDonorService;

    public DonorJourneyTests()
    {
        _mockDonorService = new Mock<IDonorService>();
        _mockDonorService.Setup(s => s.GetJourneyActivityTypesAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<DonorJourneyActivityType>
            {
                new DonorJourneyActivityType { ActivityTypeID = 1, Description = "Email - inbound" }
            });

        _mockDonorService.Setup(s => s.GetJourneyHistoryAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new List<DonorJourneyEntry>());

        _mockDonorService.Setup(s => s.GetOrganizationUsersAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<UserItem>
            {
                new UserItem { UserID = 1, UserName = "Test User" }
            });

        _mockDonorService.Setup(s => s.GetByIDAsync(It.IsAny<int>()))
            .ReturnsAsync(new DonorItem
            {
                EMADonorID = 100,
                EMAOrganizationID = 15,
                OfficialDonorName = "Test Donor",
                EmailAddress = "test@example.com"
            });

        var mockCustomization = new Mock<ICustomizationService>();
        mockCustomization.Setup(s => s.GetDonorStatusesAsync(It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync(new List<DonorStatusItem>
            {
                new DonorStatusItem { DonorStatusID = 1, Description = "Active Donor" }
            });

        var mockSessionState = new Mock<IUserSessionStateService>();
        mockSessionState.Setup(s => s.CurrentSession).Returns(new UserSession { UserID = 1, UserName = "Test User" });

        var mockJsRuntime = new Mock<IJSRuntime>();

        Services.AddSingleton(_mockDonorService.Object);
        Services.AddSingleton(mockCustomization.Object);
        Services.AddSingleton(mockSessionState.Object);
        Services.AddSingleton(mockJsRuntime.Object);
    }

    // Verifies the page route is set to /donor-journey
    [Fact]
    public void DonorJourney_HasCorrectPageRouteAttribute()
    {
        var routeAttr = typeof(Journey).GetCustomAttribute<RouteAttribute>();

        Assert.NotNull(routeAttr);
        Assert.Equal("/donor-journey", routeAttr.Template);
    }

    // Checks that the donor journey page renders without errors
    [Fact]
    public void DonorJourneyComponent_RendersContainerAndFormFields()
    {
        var cut = RenderComponent<Journey>();

        Assert.NotNull(cut);
        Assert.Contains("donors-container", cut.Markup);
        Assert.Contains("donors-form", cut.Markup);
    }

    // Checks that clicking check duplicates shows the email validation message
    [Fact]
    public void DonorJourney_ClickingCheckDuplicates_DisplaysValidationMessage()
    {
        var cut = RenderComponent<Journey>();

        var checkDuplicatesBtn = cut.Find("button.btn-create");
        checkDuplicatesBtn.Click();

        Assert.Contains("Please enter an email to check.", cut.Markup);
    }

    // Verifies typing in the search box calls the donor search service
    [Fact]
    public void DonorJourney_TypingInSearchInput_TriggersDonorSearch()
    {
        _mockDonorService.Setup(s => s.SearchAsync(
                It.IsAny<int>(),
                "John",
                null,
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DonorItem>
            {
                new DonorItem { EMADonorID = 101, OfficialDonorName = "John Doe" }
            });

        var cut = RenderComponent<Journey>();

        var searchInput = cut.Find("input.typeahead");
        searchInput.Input("John");

        _mockDonorService.Verify(s => s.SearchAsync(
                It.IsAny<int>(),
                "John",
                null,
                false,
                It.IsAny<CancellationToken>()), Times.Once);
    }
}
