using EMA.Api.DTOs;
using EMA.Api.Functions.Organization;
using EMA.Api.Services.ScreenFeedback;
using EMA.Api.Services.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EMA.Api.Tests.Organization;

public sealed class Task39Point1JourneyAuthorizationTests
{
    [Fact]
    public async Task FeedbackHistoryRequiresExactOrganizationJourneyRoutePermission()
    {
        var repository = new Mock<IScreenFeedbackRepository>(MockBehavior.Strict);
        var authorization = new Mock<IScreenFeedbackAuthorizationService>();
        authorization
            .Setup(item => item.CanAccessInsiderRouteAsync(
                It.IsAny<ApiRequestIdentity>(),
                "/organization-journey",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var function = new GetOrganizationJourneyFeedback(
            NullLogger<GetOrganizationJourneyFeedback>.Instance,
            repository.Object,
            authorization.Object);

        var result = await function.Run(
            Request(Identity(1, 7)),
            25);

        Assert.IsType<ForbidResult>(result);
        repository.VerifyNoOtherCalls();
        authorization.Verify(
            item => item.CanAccessInsiderRouteAsync(
                It.IsAny<ApiRequestIdentity>(),
                "/organization-journey",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task FeedbackHistoryLoadsAfterExactOrganizationJourneyPermissionPasses()
    {
        var expected = new OrganizationJourneyFeedbackSummaryDto();
        var repository = new Mock<IScreenFeedbackRepository>();
        repository
            .Setup(item => item.GetOrganizationJourneyFeedbackAsync(
                25,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var authorization = new Mock<IScreenFeedbackAuthorizationService>();
        authorization
            .Setup(item => item.CanAccessInsiderRouteAsync(
                It.IsAny<ApiRequestIdentity>(),
                "/organization-journey",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var function = new GetOrganizationJourneyFeedback(
            NullLogger<GetOrganizationJourneyFeedback>.Instance,
            repository.Object,
            authorization.Object);

        var result = await function.Run(
            Request(Identity(1, 7)),
            25);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(expected, ok.Value);
        repository.VerifyAll();
    }

    [Fact]
    public void FeedbackEndpointUsesOrganizationUserMiddlewareBoundary()
    {
        var policy = new ApiEndpointPolicyRegistry()
            .GetPolicy("GetOrganizationJourneyFeedback");

        Assert.Equal(ApiEndpointAccess.OrganizationUser, policy.Access);
    }

    private static HttpRequest Request(ApiRequestIdentity identity)
    {
        var context = new DefaultHttpContext
        {
            User = identity.ToClaimsPrincipal()
        };

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
            RoleIDs: [9000],
            AllowedCategoryIDs: [90],
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddMinutes(15));
}
