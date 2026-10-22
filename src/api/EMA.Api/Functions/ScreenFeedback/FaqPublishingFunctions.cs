using System.Text;
using EMA.Api.DTOs;
using EMA.Api.Services;
using EMA.Api.Services.ScreenFeedback;
using EMA.Api.Services.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.ScreenFeedback;

public sealed class FaqPublishingFunctions
{
    private const int MaximumQuestionLength = 256;
    private const int MaximumAnswerLength = 20_000;
    private const string InsiderFaqRoute = "/ww-faqs";

    private readonly ILogger<FaqPublishingFunctions> _logger;
    private readonly IScreenFeedbackRepository _repository;
    private readonly IScreenFeedbackAuthorizationService _authorization;
    private readonly IBlobStorageService _blobStorageService;

    public FaqPublishingFunctions(
        ILogger<FaqPublishingFunctions> logger,
        IScreenFeedbackRepository repository,
        IScreenFeedbackAuthorizationService authorization,
        IBlobStorageService blobStorageService)
    {
        _logger = logger;
        _repository = repository;
        _authorization = authorization;
        _blobStorageService = blobStorageService;
    }

    [Function("CreateInsiderFaq")]
    public async Task<IActionResult> CreateInsiderFaqAsync(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "post",
            Route = "insider/faqs")]
        HttpRequest request)
    {
        try
        {
            var identity = request.GetRequiredApiIdentity();

            if (!await _authorization.CanAccessInsiderRouteAsync(
                    identity,
                    InsiderFaqRoute,
                    request.HttpContext.RequestAborted))
            {
                return new ForbidResult();
            }

            var payload = await request.ReadFromJsonAsync<CreateFaqRequestDto>(
                cancellationToken: request.HttpContext.RequestAborted);

            if (payload is null)
            {
                return new BadRequestObjectResult(
                    new { message = "FAQ data is required." });
            }

            var question = Normalize(payload.Question);
            var answer = NormalizeLongText(payload.Answer) ?? string.Empty;

            if (string.IsNullOrWhiteSpace(question) || question.Length > MaximumQuestionLength)
            {
                return new BadRequestObjectResult(
                    new { message = "Question is required and must be 256 characters or less." });
            }

            if (answer.Length > MaximumAnswerLength)
            {
                return new BadRequestObjectResult(
                    new { message = $"Answer must be {MaximumAnswerLength:N0} characters or less." });
            }

            var faqID = await _repository.CreateGlobalFaqAsync(
                identity.EMAOrganizationID,
                identity.UserID,
                question,
                answer,
                request.HttpContext.RequestAborted);

            return new OkObjectResult(
                new CreateScreenFeedbackResponseDto
                {
                    ID = faqID
                });
        }
        catch (UnauthorizedAccessException)
        {
            return AuthenticationRequired();
        }
        catch (Exception exception)
        {
            return LogServerError(
                exception,
                "creating a global Insider FAQ");
        }
    }

    [Function("PublishPublicFaqPage")]
    public async Task<IActionResult> PublishPublicFaqPageAsync(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "post",
            Route = "insider/faqs/publish")]
        HttpRequest request)
    {
        try
        {
            var identity = request.GetRequiredApiIdentity();

            if (!await _authorization.CanAccessInsiderRouteAsync(
                    identity,
                    InsiderFaqRoute,
                    request.HttpContext.RequestAborted))
            {
                return new ForbidResult();
            }

            var faqs = await _repository.GetPublicFaqsAsync(
                request.HttpContext.RequestAborted);

            var html = PublicFaqHtmlBuilder.Build(faqs);
            await using var content = new MemoryStream(
                Encoding.UTF8.GetBytes(html),
                writable: false);

            await _blobStorageService.BlobPutAsync(
                PublicFaqHtmlBuilder.PublishedBlobID,
                content,
                "text/html; charset=utf-8",
                request.HttpContext.RequestAborted);

            return new OkObjectResult(
                new PublicFaqPublishResponseDto
                {
                    PublishedFaqCount = faqs.Count,
                    PublishedAtUtc = DateTime.UtcNow
                });
        }
        catch (UnauthorizedAccessException)
        {
            return AuthenticationRequired();
        }
        catch (Exception exception)
        {
            return LogServerError(
                exception,
                "publishing the public FAQ page");
        }
    }

    [Function("GetPublishedPublicFaqPage")]
    public async Task<IActionResult> GetPublishedPublicFaqPageAsync(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "public/faqs/page")]
        HttpRequest request)
    {
        try
        {
            var blob = await _blobStorageService.BlobGetAsync(
                PublicFaqHtmlBuilder.PublishedBlobID,
                request.HttpContext.RequestAborted);

            return new FileStreamResult(
                blob.Content,
                string.IsNullOrWhiteSpace(blob.ContentType)
                    ? "text/html; charset=utf-8"
                    : blob.ContentType);
        }
        catch (FileNotFoundException)
        {
            return new NotFoundObjectResult(
                new { message = "The public FAQ page has not been published yet." });
        }
        catch (Exception exception)
        {
            return LogServerError(
                exception,
                "loading the published public FAQ page");
        }
    }

    private IActionResult LogServerError(
        Exception exception,
        string operation)
    {
        _logger.LogError(exception, "Error {Operation}.", operation);

        return new ObjectResult(
            new { message = "Unable to complete the FAQ request." })
        {
            StatusCode = StatusCodes.Status500InternalServerError
        };
    }

    private static UnauthorizedObjectResult AuthenticationRequired() =>
        new(new { message = "Authentication required." });

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();

    private static string? NormalizeLongText(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
}
