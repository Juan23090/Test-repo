using System.Text;
using EMA.Api.DTOs;
using EMA.Api.Functions.ScreenFeedback;
using EMA.Api.Services;
using EMA.Api.Services.ScreenFeedback;
using EMA.Api.Services.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EMA.Api.Tests.ScreenFeedback;

public sealed class Task41PublicFaqPublishingTests
{
    [Fact]
    public async Task CreateGlobalFaqUsesAuthenticatedInsiderAndNoMenuItem()
    {
        var repository = new Mock<IScreenFeedbackRepository>();
        repository
            .Setup(item => item.CreateGlobalFaqAsync(
                1,
                7,
                "General question",
                "General answer",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(501);

        var function = CreateFunction(repository.Object);
        var result = await function.CreateInsiderFaqAsync(
            Request(
                "{\"question\":\"General question\",\"answer\":\"General answer\"}",
                Identity(1, 7)));

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<CreateScreenFeedbackResponseDto>(ok.Value);
        Assert.Equal(501, response.ID);
        repository.VerifyAll();
    }

    [Fact]
    public async Task CreateGlobalFaqRequiresExactFaqRoutePermission()
    {
        var repository = new Mock<IScreenFeedbackRepository>(MockBehavior.Strict);
        var authorization = new Mock<IScreenFeedbackAuthorizationService>();
        authorization
            .Setup(item => item.CanAccessInsiderRouteAsync(
                It.IsAny<ApiRequestIdentity>(),
                "/ww-faqs",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var function = CreateFunction(
            repository.Object,
            authorization.Object);

        var result = await function.CreateInsiderFaqAsync(
            Request(
                "{\"question\":\"General question\",\"answer\":\"General answer\"}",
                Identity(1, 7)));

        Assert.IsType<ForbidResult>(result);
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PublishWritesOnlyPublicEligibleFaqsToFixedHtmlBlob()
    {
        var repository = new Mock<IScreenFeedbackRepository>();
        repository
            .Setup(item => item.GetPublicFaqsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PublicFaqDto>
            {
                new()
                {
                    FAQID = 1,
                    MenuItemID = null,
                    ScreenTitle = "General",
                    Question = "How do I start?",
                    Answer = "Use the setup guide."
                }
            });

        string? capturedHtml = null;
        var blobStorage = new Mock<IBlobStorageService>();
        blobStorage
            .Setup(item => item.BlobPutAsync(
                PublicFaqHtmlBuilder.PublishedBlobID,
                It.IsAny<Stream>(),
                "text/html; charset=utf-8",
                It.IsAny<CancellationToken>()))
            .Callback<string, Stream, string, CancellationToken>((_, stream, _, _) =>
            {
                using var reader = new StreamReader(
                    stream,
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true,
                    bufferSize: 1024,
                    leaveOpen: true);
                capturedHtml = reader.ReadToEnd();
                stream.Position = 0;
            })
            .Returns(Task.CompletedTask);

        var function = CreateFunction(
            repository.Object,
            blobStorage: blobStorage.Object);

        var result = await function.PublishPublicFaqPageAsync(
            Request(string.Empty, Identity(1, 7)));

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<PublicFaqPublishResponseDto>(ok.Value);
        Assert.Equal(1, response.PublishedFaqCount);
        Assert.Contains("<details", capturedHtml);
        Assert.Contains("How do I start?", capturedHtml);
        repository.VerifyAll();
        blobStorage.VerifyAll();
    }

    [Fact]
    public async Task PublishedFaqPageIsReadableWithoutRequestIdentity()
    {
        var repository = new Mock<IScreenFeedbackRepository>(MockBehavior.Strict);
        var blobStorage = new Mock<IBlobStorageService>();
        blobStorage
            .Setup(item => item.BlobGetAsync(
                PublicFaqHtmlBuilder.PublishedBlobID,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BlobGetResult
            {
                Content = new MemoryStream(Encoding.UTF8.GetBytes("<p>Published</p>")),
                ContentType = "text/html; charset=utf-8",
                ContentLength = 16
            });

        var function = CreateFunction(
            repository.Object,
            blobStorage: blobStorage.Object);

        var result = await function.GetPublishedPublicFaqPageAsync(
            Request(string.Empty, identity: null));

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("text/html; charset=utf-8", file.ContentType);
        blobStorage.VerifyAll();
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public void HtmlBuilderEncodesFaqContentAndUsesClickableDetails()
    {
        var html = PublicFaqHtmlBuilder.Build(
            new List<PublicFaqDto>
            {
                new()
                {
                    FAQID = 2,
                    ScreenTitle = "<General>",
                    Question = "<script>alert('x')</script>",
                    Answer = "A & B"
                }
            });

        Assert.Contains("<details class=\"public-faq-item\">", html);
        Assert.Contains("<summary>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("A &amp; B", html);
        Assert.Contains("&lt;General&gt;", html);
    }

    private static FaqPublishingFunctions CreateFunction(
        IScreenFeedbackRepository repository,
        IScreenFeedbackAuthorizationService? authorization = null,
        IBlobStorageService? blobStorage = null) =>
        new(
            NullLogger<FaqPublishingFunctions>.Instance,
            repository,
            authorization ?? AllowFaqRouteAuthorization(),
            blobStorage ?? Mock.Of<IBlobStorageService>());

    private static IScreenFeedbackAuthorizationService AllowFaqRouteAuthorization()
    {
        var authorization = new Mock<IScreenFeedbackAuthorizationService>();
        authorization
            .Setup(item => item.CanAccessInsiderRouteAsync(
                It.IsAny<ApiRequestIdentity>(),
                "/ww-faqs",
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
            RoleIDs: [9000],
            AllowedCategoryIDs: [90],
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddMinutes(15));
}
