using Bunit;
using EMA.App.Components.Pages.Public;
using EMA.App.Models;
using EMA.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace EMA.App.Tests.ScreenFeedback;

public sealed class Task41PublicationBoundaryTests : TestContext
{
    [Fact]
    public void EligibleLiveFaqIsNotShownBeforeFirstPublish()
    {
        var service = new Mock<IScreenFeedbackService>(MockBehavior.Strict);
        service
            .Setup(item => item.GetPublishedFaqHtmlAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        Services.AddSingleton(service.Object);

        var page = RenderComponent<PublicFaqs>();

        page.WaitForAssertion(() =>
            Assert.Contains("No public FAQs are currently available.", page.Markup));

        service.Verify(
            item => item.GetPublishedFaqHtmlAsync(It.IsAny<CancellationToken>()),
            Times.Once);
        service.Verify(
            item => item.GetPublicFaqsAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void LiveFaqChangesDoNotReplaceLastPublishedSnapshot()
    {
        const string publishedHtml =
            "<section><h2>General</h2><details><summary>Published question</summary><p>Published answer</p></details></section>";

        var service = new Mock<IScreenFeedbackService>(MockBehavior.Strict);
        service
            .Setup(item => item.GetPublishedFaqHtmlAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(publishedHtml);

        // If the public page queried live rows after publication, this would be the
        // newer database state. It must not be consulted until Publish creates a
        // new snapshot.
        service
            .Setup(item => item.GetPublicFaqsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new PublicFaq
                {
                    FAQID = 99,
                    ScreenTitle = "General",
                    Question = "Unpublished live change",
                    Answer = "This must stay private until Publish is run again."
                }
            ]);

        Services.AddSingleton(service.Object);

        var page = RenderComponent<PublicFaqs>();

        page.WaitForAssertion(() =>
        {
            var frame = page.Find("iframe.public-faq-published-frame");
            var srcdoc = frame.GetAttribute("srcdoc") ?? string.Empty;
            Assert.Contains("Published question", srcdoc);
            Assert.DoesNotContain("Unpublished live change", srcdoc);
        });

        service.Verify(
            item => item.GetPublishedFaqHtmlAsync(It.IsAny<CancellationToken>()),
            Times.Once);
        service.Verify(
            item => item.GetPublicFaqsAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void PublishedSnapshotLoadFailureShowsErrorWithoutReadingLiveFaqs()
    {
        var service = new Mock<IScreenFeedbackService>(MockBehavior.Strict);
        service
            .Setup(item => item.GetPublishedFaqHtmlAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Blob storage unavailable."));

        Services.AddSingleton(service.Object);

        var page = RenderComponent<PublicFaqs>();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("The public FAQ page is temporarily unavailable.", page.Markup);
            Assert.DoesNotContain("Blob storage unavailable", page.Markup);
        });

        service.Verify(
            item => item.GetPublicFaqsAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
