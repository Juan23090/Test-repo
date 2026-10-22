using System.Text.Json;
using EMA.Api.DTOs;
using EMA.Api.Services.Onboarding;
using EMA.Api.Services.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Onboarding;

public sealed class OrganizationOnboardingFunctions
{
    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true
        };

    private readonly ILogger<OrganizationOnboardingFunctions> _logger;
    private readonly IOrganizationOnboardingRepository _repository;

    public OrganizationOnboardingFunctions(
        ILogger<OrganizationOnboardingFunctions> logger,
        IOrganizationOnboardingRepository repository)
    {
        _logger = logger;
        _repository = repository;
    }

    [Function("GetOrganizationOnboardingSettings")]
    public async Task<IActionResult> GetAsync(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "organization/{orgID:int}/onboarding/donation-attributes")]
        HttpRequest request,
        int orgID)
    {
        try
        {
            var settings =
                await _repository.GetAsync(
                    orgID,
                    request.HttpContext.RequestAborted);

            return settings is null
                ? new NotFoundObjectResult(
                    new
                    {
                        message =
                            "Organization not found."
                    })
                : new OkObjectResult(
                    settings);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Unable to load Task 22 onboarding settings for organization {EMAOrganizationID}.",
                orgID);

            return new ObjectResult(
                new
                {
                    message =
                        "The onboarding settings could not be loaded."
                })
            {
                StatusCode =
                    StatusCodes.Status500InternalServerError
            };
        }
    }

    [Function("SaveOrganizationOnboardingSettings")]
    public async Task<IActionResult> SaveAsync(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "put",
            Route = "organization/{orgID:int}/onboarding/donation-attributes/{section}")]
        HttpRequest request,
        int orgID,
        string section)
    {
        SaveOrganizationOnboardingSettingsRequestDto? payload;

        try
        {
            payload =
                await JsonSerializer.DeserializeAsync<
                    SaveOrganizationOnboardingSettingsRequestDto>(
                    request.Body,
                    JsonOptions,
                    request.HttpContext.RequestAborted);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult(
                new
                {
                    message =
                        "Invalid onboarding settings request."
                });
        }

        if (payload is null)
        {
            return new BadRequestObjectResult(
                new
                {
                    message =
                        "Body is required."
                });
        }

        try
        {
            var identity =
                request.GetRequiredApiIdentity();

            var result =
                await _repository.SaveAsync(
                    orgID,
                    identity.UserID,
                    section,
                    payload,
                    request.HttpContext.RequestAborted);

            if (result.Succeeded)
            {
                return new OkObjectResult(
                    new
                    {
                        message =
                            result.Message
                    });
            }

            if (result.Conflict)
            {
                return new ConflictObjectResult(
                    new
                    {
                        message =
                            result.Message
                    });
            }

            return new BadRequestObjectResult(
                new
                {
                    message =
                        result.Message
                });
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Unable to save Task 22 {Section} onboarding settings for organization {EMAOrganizationID}.",
                section,
                orgID);

            return new ObjectResult(
                new
                {
                    message =
                        "The onboarding settings could not be saved."
                })
            {
                StatusCode =
                    StatusCodes.Status500InternalServerError
            };
        }
    }
}
