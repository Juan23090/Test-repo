using EMA.Api.Services.ScreenFeedback;
using EMA.Api.Services.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Organization;

public sealed class GetOrganizationJourneyFeedback
{
    private const string OrganizationJourneyRoute = "/organization-journey";

    private readonly ILogger<GetOrganizationJourneyFeedback> _logger;
    private readonly IScreenFeedbackRepository _repository;
    private readonly IScreenFeedbackAuthorizationService _authorization;

    public GetOrganizationJourneyFeedback(
        ILogger<GetOrganizationJourneyFeedback> logger,
        IScreenFeedbackRepository repository,
        IScreenFeedbackAuthorizationService authorization)
    {
        _logger = logger;
        _repository = repository;
        _authorization = authorization;
    }

    [Function("GetOrganizationJourneyFeedback")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "organization/journey-feedback/{orgID:int}")] HttpRequest request,
        int orgID)
    {
        if (orgID <= 0)
        {
            return new BadRequestObjectResult(new { message = "A valid organization ID is required." });
        }

        try
        {
            var identity = request.GetRequiredApiIdentity();

            if (!await _authorization.CanAccessInsiderRouteAsync(
                    identity,
                    OrganizationJourneyRoute,
                    request.HttpContext.RequestAborted))
            {
                return new ForbidResult();
            }

            var result = await _repository.GetOrganizationJourneyFeedbackAsync(
                orgID,
                request.HttpContext.RequestAborted);
            return new OkObjectResult(result);
        }
        catch (UnauthorizedAccessException)
        {
            return new UnauthorizedObjectResult(new { message = "Authentication is required." });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error loading Task 39.1 feedback history for organization {OrgID}.", orgID);
            return new ObjectResult(new { message = "Unable to load organization feedback history." })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }
}
