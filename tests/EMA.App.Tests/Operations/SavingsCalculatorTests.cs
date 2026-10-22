using Bunit;
using EMA.App.Components.Pages.Operations;
using EMA.App.Models;
using EMA.App.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Moq;
using System.Globalization;
using System.Reflection;
using System.Threading;
using Xunit;

namespace EMA.App.Tests.Operations;

public class SavingsCalculatorTests : TestContext
{
    private readonly Mock<ISavingsCalculatorService> _mockSavingsService;

    public SavingsCalculatorTests()
    {
        // Enforce consistent en-US culture for currency formatting ($1,800) across all OS platforms (Windows, Linux CI)
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-US");

        _mockSavingsService = new Mock<ISavingsCalculatorService>();
        _mockSavingsService.Setup(s => s.GetChMSListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ChMSOption>());

        _mockSavingsService.Setup(s => s.GetCalculatorDataAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SavingsCalculatorData
            {
                OrganizationName = "Test Church",
                MonthlySoftwareRental = 150m,
                NumberOfMonths = 12,
                LaborCostPerHour = 30m,
                Details = new List<SavingsCalculatorDetailRow>()
            });

        _mockSavingsService.Setup(s => s.GeneratePdfAsync(It.IsAny<SavingsCalculatorPdfRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 1, 2, 3 });

        _mockSavingsService.Setup(s => s.SendEmailAsync(It.IsAny<SavingsCalculatorEmailRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var mockOrgState = new Mock<IOrganizationStateService>();
        mockOrgState.Setup(s => s.CurrentOrganization)
            .Returns(new OrganizationSummary { EMAOrganizationID = 15, OrganizationName = "Test Church" });

        var mockSessionState = new Mock<IUserSessionStateService>();
        mockSessionState.Setup(s => s.CurrentSession)
            .Returns(new UserSession { UserID = 1, EMAOrganizationID = 15, UserName = "Test User" });

        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddSingleton(_mockSavingsService.Object);
        Services.AddSingleton(mockOrgState.Object);
        Services.AddSingleton(mockSessionState.Object);
    }

    // Verifies the page route is set to /calculate-your-savings
    [Fact]
    public void SavingsCalculator_HasCorrectPageRouteAttribute()
    {
        var routeAttr = typeof(Savings).GetCustomAttribute<RouteAttribute>();

        Assert.NotNull(routeAttr);
        Assert.Equal("/calculate-your-savings", routeAttr.Template);
    }

    // Verifies software rental total displays formatted calculation result
    [Fact]
    public void SavingsCalculatorComponent_ExecutesSoftwareRentalCalculation_AndRendersFormattedTotal()
    {
        var cut = RenderComponent<Savings>();

        Assert.NotNull(cut);
        Assert.Contains("savings-container", cut.Markup);
        Assert.Contains("Software Rental", cut.Markup);
        Assert.Contains("software-rental-row", cut.Markup);
        Assert.Contains("$1,800", cut.Markup);
    }

    // Checks that clicking save as pdf calls the pdf service
    [Fact]
    public void SavingsCalculator_ClickingSaveAsPdf_CallsGeneratePdfAsync()
    {
        var cut = RenderComponent<Savings>();

        var buttons = cut.FindAll("button.savings-btn");
        var pdfBtn = buttons.First(b => b.TextContent.Contains("Save as PDF"));

        pdfBtn.Click();

        _mockSavingsService.Verify(s => s.GeneratePdfAsync(It.IsAny<SavingsCalculatorPdfRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // Verifies clicking email opens the modal and sends the report
    [Fact]
    public void SavingsCalculator_ClickingEmailButton_OpensModalAndCallsSendEmailAsync()
    {
        var cut = RenderComponent<Savings>();

        var buttons = cut.FindAll("button.savings-btn");
        var emailBtn = buttons.First(b => b.TextContent.Contains("Email"));
        emailBtn.Click();

        var emailInput = cut.Find("input[type='email']");
        emailInput.Change("admin@testchurch.org");

        var modalButtons = cut.FindAll("button");
        var sendBtn = modalButtons.First(b => b.TextContent.Trim() == "Send");
        sendBtn.Click();

        _mockSavingsService.Verify(s => s.SendEmailAsync(It.IsAny<SavingsCalculatorEmailRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // Checks that clicking print calls the browser print function
    [Fact]
    public void SavingsCalculator_ClickingPrintButton_TriggersBrowserPrintInterop()
    {
        var cut = RenderComponent<Savings>();

        var buttons = cut.FindAll("button.savings-btn");
        var printBtn = buttons.First(b => b.TextContent.Contains("Print"));

        printBtn.Click();

        JSInterop.VerifyInvoke("printPage");
    }

    // Verifies clicking cancel closes the email modal
    [Fact]
    public void SavingsCalculator_ClickingCancelInEmailModal_ClosesModal()
    {
        var cut = RenderComponent<Savings>();

        var buttons = cut.FindAll("button.savings-btn");
        var emailBtn = buttons.First(b => b.TextContent.Contains("Email"));
        emailBtn.Click();

        Assert.Contains("Email Calculator Report", cut.Markup);

        var cancelBtn = cut.Find("button.btn-secondary");
        cancelBtn.Click();

        Assert.DoesNotContain("Email Calculator Report", cut.Markup);
    }
}
