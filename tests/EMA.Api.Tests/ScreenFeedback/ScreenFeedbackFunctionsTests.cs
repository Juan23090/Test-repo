using System.Text;
using EMA.Api.DTOs;
using EMA.Api.Functions.ScreenFeedback;
using EMA.Api.Services.ScreenFeedback;
using EMA.Api.Services.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EMA.Api.Tests.ScreenFeedback;

public sealed class ScreenFeedbackFunctionsTests
{
    [Fact]
    public async Task CreateFaqUsesAuthenticatedOrganizationUserAndCurrentMenuItem()
    {
        var repository = new Mock<IScreenFeedbackRepository>();
        repository
            .Setup(item => item.MenuItemExistsAsync(321, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        repository
            .Setup(item => item.CreateFaqAsync(
                321,
                18,
                7,
                "Why?",
                "Because.",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(45);

        var function = CreateFunction(repository.Object);
        var result = await function.CreateScreenFaqAsync(
            Request("{\"question\":\"Why?\",\"answer\":\"Because.\"}", Identity(18, 7)),
            321);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<CreateScreenFeedbackResponseDto>(ok.Value);
        Assert.Equal(45, response.ID);
        repository.VerifyAll();
    }

    [Fact]
    public async Task InvalidFaqIsRejectedBeforePersistence()
    {
        var repository = new Mock<IScreenFeedbackRepository>(MockBehavior.Strict);
        var function = CreateFunction(repository.Object);

        var result = await function.CreateScreenFaqAsync(
            Request("{\"question\":\"   \",\"answer\":\"\"}", Identity(18, 7)),
            321);

        Assert.IsType<BadRequestObjectResult>(result);
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EnhancementRequestUsesAuthenticatedOrganizationAndUser()
    {
        var repository = new Mock<IScreenFeedbackRepository>();
        repository
            .Setup(item => item.MenuItemExistsAsync(321, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        repository
            .Setup(item => item.CreateEnhancementRequestAsync(
                321,
                18,
                7,
                "Add feature",
                "Please add this feature",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(46);

        var function = CreateFunction(repository.Object);
        var result = await function.CreateScreenEnhancementRequestAsync(
            Request("{\"title\":\"Add feature\",\"description\":\"Please add this feature\"}", Identity(18, 7)),
            321);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<CreateScreenFeedbackResponseDto>(ok.Value);
        Assert.Equal(46, response.ID);
        repository.VerifyAll();
    }

    [Fact]
    public async Task SummaryUsesAuthenticatedOrganization()
    {
        var repository = new Mock<IScreenFeedbackRepository>();
        repository
            .Setup(item => item.MenuItemExistsAsync(321, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        repository
            .Setup(item => item.GetScreenActionSummaryAsync(321, 18, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScreenActionSummaryDto { MenuItemID = 321 });

        var function = CreateFunction(repository.Object);
        var result = await function.GetScreenActionSummaryAsync(
            Request(string.Empty, Identity(18, 7)),
            321);

        Assert.IsType<OkObjectResult>(result);
        repository.VerifyAll();
    }

    [Fact]
    public async Task EnhancementVoteUsesOneOrganizationVoteKey()
    {
        var repository = new Mock<IScreenFeedbackRepository>();
        repository
            .Setup(item => item.SaveEnhancementVoteAsync(
                99,
                18,
                7,
                "N",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var function = CreateFunction(repository.Object);
        var result = await function.SaveScreenEnhancementVoteAsync(
            Request("{\"vote\":\"N\"}", Identity(18, 7)),
            99);

        Assert.IsType<OkResult>(result);
        repository.VerifyAll();
    }

    [Fact]
    public async Task SupportRequestUsesAuthenticatedOrganizationAndUser()
    {
        var repository = new Mock<IScreenFeedbackRepository>();
        repository
            .Setup(item => item.MenuItemExistsAsync(321, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        repository
            .Setup(item => item.CreateSupportTicketAsync(
                321,
                18,
                7,
                "Save error",
                "Donation will not save",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(501);

        var function = CreateFunction(repository.Object);
        var result = await function.CreateScreenSupportTicketAsync(
            Request("{\"title\":\"Save error\",\"description\":\"Donation will not save\"}", Identity(18, 7)),
            321);

        Assert.IsType<OkObjectResult>(result);
        repository.VerifyAll();
    }

    [Fact]
    public async Task CreateFaqForInaccessibleMenuItemIsForbiddenBeforePersistence()
    {
        var repository = new Mock<IScreenFeedbackRepository>();
        repository
            .Setup(item => item.MenuItemExistsAsync(321, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var authorization = new Mock<IScreenFeedbackAuthorizationService>();
        authorization
            .Setup(item => item.CanAccessMenuItemAsync(
                It.IsAny<ApiRequestIdentity>(),
                321,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var function = CreateFunction(repository.Object, authorization.Object);
        var result = await function.CreateScreenFaqAsync(
            Request("{\"question\":\"Why?\",\"answer\":\"Because.\"}", Identity(18, 7)),
            321);

        Assert.IsType<ForbidResult>(result);
        repository.Verify(
            item => item.CreateFaqAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task MissingIdentityReturnsUnauthorizedInsteadOfServerError()
    {
        var repository = new Mock<IScreenFeedbackRepository>(MockBehavior.Strict);
        var authorization = new Mock<IScreenFeedbackAuthorizationService>(MockBehavior.Strict);
        var function = CreateFunction(repository.Object, authorization.Object);

        var result = await function.CreateScreenFaqAsync(
            Request("{\"question\":\"Why?\",\"answer\":\"Because.\"}", identity: null),
            321);

        Assert.IsType<UnauthorizedObjectResult>(result);
        repository.VerifyNoOtherCalls();
        authorization.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InsiderFaqApiRequiresSpecificFaqRoutePermission()
    {
        var repository = new Mock<IScreenFeedbackRepository>(MockBehavior.Strict);
        var authorization = new Mock<IScreenFeedbackAuthorizationService>();
        authorization
            .Setup(item => item.CanAccessInsiderRouteAsync(
                It.IsAny<ApiRequestIdentity>(),
                "/ww-faqs",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var function = CreateFunction(repository.Object, authorization.Object);
        var result = await function.GetInsiderFaqsAsync(
            Request(string.Empty, Identity(1, 7)));

        Assert.IsType<ForbidResult>(result);
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PublicFaqsDoNotRequireRequestIdentity()
    {
        var repository = new Mock<IScreenFeedbackRepository>();
        repository
            .Setup(item => item.GetPublicFaqsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PublicFaqDto>
            {
                new() { FAQID = 1, MenuItemID = 321, Question = "Q", Answer = "A" }
            });

        var function = CreateFunction(repository.Object);
        var result = await function.GetPublicFaqsAsync(Request(string.Empty, identity: null));

        var ok = Assert.IsType<OkObjectResult>(result);
        var items = Assert.IsAssignableFrom<IReadOnlyList<PublicFaqDto>>(ok.Value);
        Assert.Single(items);
    }

    private static ScreenFeedbackFunctions CreateFunction(
        IScreenFeedbackRepository repository,
        IScreenFeedbackAuthorizationService? authorization = null) =>
        new(
            NullLogger<ScreenFeedbackFunctions>.Instance,
            repository,
            authorization ?? AllowAllAuthorization());

    private static IScreenFeedbackAuthorizationService AllowAllAuthorization()
    {
        var authorization = new Mock<IScreenFeedbackAuthorizationService>();
        authorization
            .Setup(item => item.CanAccessMenuItemAsync(
                It.IsAny<ApiRequestIdentity>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        authorization
            .Setup(item => item.CanAccessEnhancementRequestAsync(
                It.IsAny<ApiRequestIdentity>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        authorization
            .Setup(item => item.CanAccessInsiderRouteAsync(
                It.IsAny<ApiRequestIdentity>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return authorization.Object;
    }

    private static HttpRequest Request(
        string body,
        ApiRequestIdentity? identity)
    {
        var context = new DefaultHttpContext();

        if (identity is not null)
        {
            context.User = identity.ToClaimsPrincipal();
        }

        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Request.ContentType = "application/json";
        return context.Request;
    }

    private static ApiRequestIdentity Identity(
        int organizationID,
        int userID) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ApiActorType.OrganizationUser,
            organizationID,
            userID,
            DonorID: null,
            RoleIDs: [10],
            AllowedCategoryIDs: [20, 90],
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddMinutes(15));
}
