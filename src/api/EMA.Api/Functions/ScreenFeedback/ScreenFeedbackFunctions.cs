using EMA.Api.DTOs;
using EMA.Api.Services.ScreenFeedback;
using EMA.Api.Services.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.ScreenFeedback;

public sealed class ScreenFeedbackFunctions
{
    private const int MaximumTitleLength = 256;
    private const int MaximumLongTextLength = 20_000;
    private const string InsiderFaqRoute = "/ww-faqs";
    private const string InsiderEnhancementRoute = "/ww-enhancements";
    private const string InsiderSupportRoute = "/ww-tickets";

    private readonly ILogger<ScreenFeedbackFunctions> _logger;
    private readonly IScreenFeedbackRepository _repository;
    private readonly IScreenFeedbackAuthorizationService _authorization;

    public ScreenFeedbackFunctions(
        ILogger<ScreenFeedbackFunctions> logger,
        IScreenFeedbackRepository repository,
        IScreenFeedbackAuthorizationService authorization)
    {
        _logger = logger;
        _repository = repository;
        _authorization = authorization;
    }

    [Function("GetScreenActionSummary")]
    public async Task<IActionResult> GetScreenActionSummaryAsync(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "screen-actions/{menuItemID:int}")]
        HttpRequest request,
        int menuItemID)
    {
        if (menuItemID <= 0)
        {
            return new BadRequestObjectResult(
                new { message = "A valid MenuItem ID is required." });
        }

        try
        {
            var identity =
                request.GetRequiredApiIdentity();

            if (!await _repository.MenuItemExistsAsync(
                    menuItemID,
                    request.HttpContext.RequestAborted))
            {
                return new NotFoundObjectResult(
                    new { message = $"MenuItem {menuItemID} was not found." });
            }

            if (!await _authorization.CanAccessMenuItemAsync(
                    identity,
                    menuItemID,
                    request.HttpContext.RequestAborted))
            {
                return new ForbidResult();
            }

            var result =
                await _repository.GetScreenActionSummaryAsync(
                    menuItemID,
                    identity.EMAOrganizationID,
                    request.HttpContext.RequestAborted);

            return new OkObjectResult(result);
        }
        catch (UnauthorizedAccessException)
        {
            return AuthenticationRequired();
        }
        catch (Exception exception)
        {
            return LogServerError(
                exception,
                "loading screen actions for MenuItem {MenuItemID}",
                menuItemID);
        }
    }

    [Function("CreateScreenFaq")]
    public async Task<IActionResult> CreateScreenFaqAsync(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "post",
            Route = "screen-actions/{menuItemID:int}/faqs")]
        HttpRequest request,
        int menuItemID)
    {
        if (menuItemID <= 0)
        {
            return BadRequest("A valid MenuItem ID is required.");
        }

        try
        {
            var identity = request.GetRequiredApiIdentity();

            var payload =
                await request.ReadFromJsonAsync<CreateFaqRequestDto>(
                    cancellationToken:
                        request.HttpContext.RequestAborted);

            if (payload is null)
            {
                return BadRequest("FAQ data is required.");
            }

            var question = Normalize(payload.Question);
            var answer = NormalizeLongText(payload.Answer) ?? string.Empty;

            if (!IsRequiredShortTextValid(question))
            {
                return BadRequest(
                    "Question is required and must be 256 characters or less.");
            }

            if (answer.Length > MaximumLongTextLength)
            {
                return BadRequest(
                    $"Answer must be {MaximumLongTextLength:N0} characters or less.");
            }

            if (!await _repository.MenuItemExistsAsync(
                    menuItemID,
                    request.HttpContext.RequestAborted))
            {
                return new NotFoundObjectResult(
                    new { message = $"MenuItem {menuItemID} was not found." });
            }

            if (!await _authorization.CanAccessMenuItemAsync(
                    identity,
                    menuItemID,
                    request.HttpContext.RequestAborted))
            {
                return new ForbidResult();
            }

            var id = await _repository.CreateFaqAsync(
                menuItemID,
                identity.EMAOrganizationID,
                identity.UserID,
                question!,
                answer,
                request.HttpContext.RequestAborted);

            return new OkObjectResult(
                new CreateScreenFeedbackResponseDto
                {
                    ID = id
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
                "creating an FAQ for MenuItem {MenuItemID}",
                menuItemID);
        }
    }

    [Function("CreateScreenEnhancementRequest")]
    public async Task<IActionResult> CreateScreenEnhancementRequestAsync(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "post",
            Route = "screen-actions/{menuItemID:int}/enhancements")]
        HttpRequest request,
        int menuItemID)
    {
        if (menuItemID <= 0)
        {
            return BadRequest("A valid MenuItem ID is required.");
        }

        try
        {
            var identity = request.GetRequiredApiIdentity();

            var payload =
                await request.ReadFromJsonAsync<CreateEnhancementRequestDto>(
                    cancellationToken:
                        request.HttpContext.RequestAborted);

            if (payload is null)
            {
                return BadRequest("Enhancement request data is required.");
            }

            var title = Normalize(payload.Title);
            var description = NormalizeLongText(payload.Description);

            if (!IsRequiredShortTextValid(title))
            {
                return BadRequest(
                    "Title is required and must be 256 characters or less.");
            }

            if (!IsRequiredLongTextValid(description))
            {
                return BadRequest(
                    $"Description is required and must be {MaximumLongTextLength:N0} characters or less.");
            }

            if (!await _repository.MenuItemExistsAsync(
                    menuItemID,
                    request.HttpContext.RequestAborted))
            {
                return new NotFoundObjectResult(
                    new { message = $"MenuItem {menuItemID} was not found." });
            }

            if (!await _authorization.CanAccessMenuItemAsync(
                    identity,
                    menuItemID,
                    request.HttpContext.RequestAborted))
            {
                return new ForbidResult();
            }

            var id = await _repository.CreateEnhancementRequestAsync(
                menuItemID,
                identity.EMAOrganizationID,
                identity.UserID,
                title!,
                description!,
                request.HttpContext.RequestAborted);

            return new OkObjectResult(
                new CreateScreenFeedbackResponseDto
                {
                    ID = id
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
                "creating an enhancement request for MenuItem {MenuItemID}",
                menuItemID);
        }
    }

    [Function("CreateScreenSupportTicket")]
    public async Task<IActionResult> CreateScreenSupportTicketAsync(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "post",
            Route = "screen-actions/{menuItemID:int}/support")]
        HttpRequest request,
        int menuItemID)
    {
        if (menuItemID <= 0)
        {
            return BadRequest("A valid MenuItem ID is required.");
        }

        try
        {
            var identity = request.GetRequiredApiIdentity();

            var payload =
                await request.ReadFromJsonAsync<CreateSupportTicketRequestDto>(
                    cancellationToken:
                        request.HttpContext.RequestAborted);

            if (payload is null)
            {
                return BadRequest("Support request data is required.");
            }

            var title = Normalize(payload.Title);
            var description = NormalizeLongText(payload.Description);

            if (!IsRequiredShortTextValid(title))
            {
                return BadRequest(
                    "Title is required and must be 256 characters or less.");
            }

            if (!IsRequiredLongTextValid(description))
            {
                return BadRequest(
                    $"Description is required and must be {MaximumLongTextLength:N0} characters or less.");
            }

            if (!await _repository.MenuItemExistsAsync(
                    menuItemID,
                    request.HttpContext.RequestAborted))
            {
                return new NotFoundObjectResult(
                    new { message = $"MenuItem {menuItemID} was not found." });
            }

            if (!await _authorization.CanAccessMenuItemAsync(
                    identity,
                    menuItemID,
                    request.HttpContext.RequestAborted))
            {
                return new ForbidResult();
            }

            var id = await _repository.CreateSupportTicketAsync(
                menuItemID,
                identity.EMAOrganizationID,
                identity.UserID,
                title!,
                description!,
                request.HttpContext.RequestAborted);

            return new OkObjectResult(
                new CreateScreenFeedbackResponseDto
                {
                    ID = id
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
                "creating a support ticket for MenuItem {MenuItemID}",
                menuItemID);
        }
    }

    [Function("SaveScreenEnhancementVote")]
    public async Task<IActionResult> SaveScreenEnhancementVoteAsync(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "put",
            Route = "screen-actions/enhancements/{enhancementRequestID:int}/vote")]
        HttpRequest request,
        int enhancementRequestID)
    {
        if (enhancementRequestID <= 0)
        {
            return BadRequest("A valid enhancement request ID is required.");
        }

        try
        {
            var identity = request.GetRequiredApiIdentity();

            var payload =
                await request.ReadFromJsonAsync<SaveEnhancementVoteRequestDto>(
                    cancellationToken:
                        request.HttpContext.RequestAborted);

            var vote =
                Normalize(payload?.Vote)
                    ?.ToUpperInvariant();

            if (vote is not ("Y" or "N"))
            {
                return BadRequest("Vote must be Yes or No.");
            }

            if (!await _authorization.CanAccessEnhancementRequestAsync(
                    identity,
                    enhancementRequestID,
                    request.HttpContext.RequestAborted))
            {
                return new ForbidResult();
            }

            var saved = await _repository.SaveEnhancementVoteAsync(
                enhancementRequestID,
                identity.EMAOrganizationID,
                identity.UserID,
                vote,
                request.HttpContext.RequestAborted);

            return saved
                ? new OkResult()
                : new NotFoundObjectResult(
                    new { message = "Enhancement request was not found." });
        }
        catch (UnauthorizedAccessException)
        {
            return AuthenticationRequired();
        }
        catch (Exception exception)
        {
            return LogServerError(
                exception,
                "saving an enhancement vote for request {EnhancementRequestID}",
                enhancementRequestID);
        }
    }

    [Function("GetPublicFaqs")]
    public async Task<IActionResult> GetPublicFaqsAsync(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "public/faqs")]
        HttpRequest request)
    {
        try
        {
            var items = await _repository.GetPublicFaqsAsync(
                request.HttpContext.RequestAborted);

            return new OkObjectResult(items);
        }
        catch (Exception exception)
        {
            return LogServerError(
                exception,
                "loading public FAQs");
        }
    }

    [Function("GetInsiderFaqs")]
    public async Task<IActionResult> GetInsiderFaqsAsync(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
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

            var items = await _repository.GetFaqAdminAsync(
                request.HttpContext.RequestAborted);

            return new OkObjectResult(items);
        }
        catch (UnauthorizedAccessException)
        {
            return AuthenticationRequired();
        }
        catch (Exception exception)
        {
            return LogServerError(
                exception,
                "loading Insider FAQs");
        }
    }

    [Function("UpdateInsiderFaq")]
    public async Task<IActionResult> UpdateInsiderFaqAsync(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "put",
            Route = "insider/faqs/{faqID:int}")]
        HttpRequest request,
        int faqID)
    {
        if (faqID <= 0)
        {
            return BadRequest("A valid FAQ ID is required.");
        }

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

            var payload =
                await request.ReadFromJsonAsync<UpdateFaqAdminRequestDto>(
                    cancellationToken:
                        request.HttpContext.RequestAborted);

            if (payload is null)
            {
                return BadRequest("FAQ data is required.");
            }

            payload.Question = Normalize(payload.Question) ?? string.Empty;
            payload.Answer = NormalizeLongText(payload.Answer) ?? string.Empty;
            payload.Comments = Normalize(payload.Comments);

            if (!IsRequiredShortTextValid(payload.Question))
            {
                return BadRequest(
                    "Question is required and must be 256 characters or less.");
            }

            if (payload.Answer.Length > MaximumLongTextLength)
            {
                return BadRequest(
                    $"Answer must be {MaximumLongTextLength:N0} characters or less.");
            }

            if (payload.Comments?.Length > MaximumTitleLength)
            {
                return BadRequest("Comments must be 256 characters or less.");
            }

            var result = await _repository.UpdateFaqAsync(
                faqID,
                identity.UserID,
                payload,
                request.HttpContext.RequestAborted);

            return SaveResult(result, "FAQ");
        }
        catch (UnauthorizedAccessException)
        {
            return AuthenticationRequired();
        }
        catch (Exception exception)
        {
            return LogServerError(
                exception,
                "updating FAQ {FAQID}",
                faqID);
        }
    }

    [Function("GetInsiderEnhancements")]
    public async Task<IActionResult> GetInsiderEnhancementsAsync(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "insider/enhancements")]
        HttpRequest request)
    {
        try
        {
            var identity = request.GetRequiredApiIdentity();

            if (!await _authorization.CanAccessInsiderRouteAsync(
                    identity,
                    InsiderEnhancementRoute,
                    request.HttpContext.RequestAborted))
            {
                return new ForbidResult();
            }

            var result = await _repository.GetEnhancementAdminAsync(
                request.HttpContext.RequestAborted);

            return new OkObjectResult(result);
        }
        catch (UnauthorizedAccessException)
        {
            return AuthenticationRequired();
        }
        catch (Exception exception)
        {
            return LogServerError(
                exception,
                "loading Insider enhancement requests");
        }
    }

    [Function("UpdateInsiderEnhancement")]
    public async Task<IActionResult> UpdateInsiderEnhancementAsync(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "put",
            Route = "insider/enhancements/{enhancementRequestID:int}")]
        HttpRequest request,
        int enhancementRequestID)
    {
        if (enhancementRequestID <= 0)
        {
            return BadRequest("A valid enhancement request ID is required.");
        }

        try
        {
            var identity = request.GetRequiredApiIdentity();

            if (!await _authorization.CanAccessInsiderRouteAsync(
                    identity,
                    InsiderEnhancementRoute,
                    request.HttpContext.RequestAborted))
            {
                return new ForbidResult();
            }

            var payload =
                await request.ReadFromJsonAsync<UpdateEnhancementAdminRequestDto>(
                    cancellationToken:
                        request.HttpContext.RequestAborted);

            if (payload is null)
            {
                return BadRequest("Enhancement request data is required.");
            }

            payload.Title = Normalize(payload.Title) ?? string.Empty;
            payload.Description = NormalizeLongText(payload.Description) ?? string.Empty;
            payload.EMAComments = NormalizeLongText(payload.EMAComments);

            if (!IsRequiredShortTextValid(payload.Title))
            {
                return BadRequest(
                    "Title is required and must be 256 characters or less.");
            }

            if (!IsRequiredLongTextValid(payload.Description))
            {
                return BadRequest(
                    $"Description is required and must be {MaximumLongTextLength:N0} characters or less.");
            }

            if (payload.EMAComments?.Length > MaximumLongTextLength)
            {
                return BadRequest(
                    $"EMA Comments must be {MaximumLongTextLength:N0} characters or less.");
            }

            if (payload.EnhancementRequestStatusID <= 0)
            {
                return BadRequest("A valid enhancement status is required.");
            }

            var result = await _repository.UpdateEnhancementAsync(
                enhancementRequestID,
                identity.UserID,
                payload,
                request.HttpContext.RequestAborted);

            return SaveResult(result, "Enhancement request");
        }
        catch (UnauthorizedAccessException)
        {
            return AuthenticationRequired();
        }
        catch (Exception exception)
        {
            return LogServerError(
                exception,
                "updating enhancement request {EnhancementRequestID}",
                enhancementRequestID);
        }
    }

    [Function("GetInsiderSupportTickets")]
    public async Task<IActionResult> GetInsiderSupportTicketsAsync(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "insider/support-tickets")]
        HttpRequest request)
    {
        try
        {
            var identity = request.GetRequiredApiIdentity();

            if (!await _authorization.CanAccessInsiderRouteAsync(
                    identity,
                    InsiderSupportRoute,
                    request.HttpContext.RequestAborted))
            {
                return new ForbidResult();
            }

            var result = await _repository.GetSupportTicketAdminAsync(
                request.HttpContext.RequestAborted);

            return new OkObjectResult(result);
        }
        catch (UnauthorizedAccessException)
        {
            return AuthenticationRequired();
        }
        catch (Exception exception)
        {
            return LogServerError(
                exception,
                "loading Insider support tickets");
        }
    }

    [Function("UpdateInsiderSupportTicket")]
    public async Task<IActionResult> UpdateInsiderSupportTicketAsync(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "put",
            Route = "insider/support-tickets/{supportTicketID:int}")]
        HttpRequest request,
        int supportTicketID)
    {
        if (supportTicketID <= 0)
        {
            return BadRequest("A valid support ticket ID is required.");
        }

        try
        {
            var identity = request.GetRequiredApiIdentity();

            if (!await _authorization.CanAccessInsiderRouteAsync(
                    identity,
                    InsiderSupportRoute,
                    request.HttpContext.RequestAborted))
            {
                return new ForbidResult();
            }

            var payload =
                await request.ReadFromJsonAsync<UpdateSupportTicketAdminRequestDto>(
                    cancellationToken:
                        request.HttpContext.RequestAborted);

            if (payload is null)
            {
                return BadRequest("Support ticket data is required.");
            }

            payload.Title = Normalize(payload.Title) ?? string.Empty;
            payload.Description = NormalizeLongText(payload.Description) ?? string.Empty;
            payload.EMAComments = NormalizeLongText(payload.EMAComments);

            if (!IsRequiredShortTextValid(payload.Title))
            {
                return BadRequest(
                    "Title is required and must be 256 characters or less.");
            }

            if (!IsRequiredLongTextValid(payload.Description))
            {
                return BadRequest(
                    $"Description is required and must be {MaximumLongTextLength:N0} characters or less.");
            }

            if (payload.EMAComments?.Length > MaximumLongTextLength)
            {
                return BadRequest(
                    $"EMA Comments must be {MaximumLongTextLength:N0} characters or less.");
            }

            if (payload.SupportTicketStatusID <= 0)
            {
                return BadRequest("A valid support ticket status is required.");
            }

            var result = await _repository.UpdateSupportTicketAsync(
                supportTicketID,
                identity.UserID,
                payload,
                request.HttpContext.RequestAborted);

            return SaveResult(result, "Support ticket");
        }
        catch (UnauthorizedAccessException)
        {
            return AuthenticationRequired();
        }
        catch (Exception exception)
        {
            return LogServerError(
                exception,
                "updating support ticket {SupportTicketID}",
                supportTicketID);
        }
    }

    private static UnauthorizedObjectResult AuthenticationRequired() =>
        new(new { message = "Authentication is required." });

    private IActionResult LogServerError(
        Exception exception,
        string operation,
        params object?[] arguments)
    {
        _logger.LogError(
            exception,
            $"Error {operation}.",
            arguments);

        return new ObjectResult(
            new { message = "Unable to complete the screen action request." })
        {
            StatusCode = StatusCodes.Status500InternalServerError
        };
    }

    private static IActionResult SaveResult(
        ScreenFeedbackSaveResult result,
        string recordName)
    {
        return result switch
        {
            ScreenFeedbackSaveResult.Success =>
                new OkResult(),
            ScreenFeedbackSaveResult.InvalidStatus =>
                new BadRequestObjectResult(
                    new { message = "The selected status is invalid." }),
            _ =>
                new NotFoundObjectResult(
                    new { message = $"{recordName} was not found." })
        };
    }

    private static BadRequestObjectResult BadRequest(
        string message) =>
        new(new { message });

    private static bool IsRequiredShortTextValid(
        string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= MaximumTitleLength;

    private static bool IsRequiredLongTextValid(
        string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= MaximumLongTextLength;

    private static string? Normalize(
        string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();

    private static string? NormalizeLongText(
        string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
}
