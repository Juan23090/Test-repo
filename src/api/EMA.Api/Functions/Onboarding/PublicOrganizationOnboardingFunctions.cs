using System.Text.Json;
using EMA.Api.DTOs;
using EMA.Api.Services.Authentication;
using EMA.Api.Services.Onboarding;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Onboarding;

public sealed class PublicOrganizationOnboardingFunctions
{
    private readonly ILogger<PublicOrganizationOnboardingFunctions> _logger;
    private readonly IPublicOrganizationRequestRepository _repository;
    private readonly ITwoFactorCodeStore _codeStore;
    private readonly ITwoFactorEmailSender _emailSender;

    public PublicOrganizationOnboardingFunctions(
        ILogger<PublicOrganizationOnboardingFunctions> logger,
        IPublicOrganizationRequestRepository repository,
        ITwoFactorCodeStore codeStore,
        ITwoFactorEmailSender emailSender)
    {
        _logger = logger;
        _repository = repository;
        _codeStore = codeStore;
        _emailSender = emailSender;
    }

    [Function("GetPublicOnboardingLookups")]
    public async Task<IActionResult> GetLookupsAsync(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "public/onboarding/lookups")]
        HttpRequest request)
    {
        try
        {
            return new OkObjectResult(
                await _repository.GetLookupsAsync(
                    request.HttpContext.RequestAborted));
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Unable to load public onboarding lookups.");

            return ServerError("Unable to load onboarding options.");
        }
    }

    [Function("StartPublicOrganizationEmailVerification")]
    public async Task<IActionResult> StartEmailVerificationAsync(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "post",
            Route = "public/onboarding/email-verification/start")]
        HttpRequest request)
    {
        StartEmailVerificationRequestDto? body;

        try
        {
            body = await DeserializeAsync<StartEmailVerificationRequestDto>(request);
        }
        catch (JsonException)
        {
            return BadRequest("Invalid email verification request.");
        }

        if (body is null ||
            body.Purpose != TwoFactorPurpose.NewOrganizationAdminEmail ||
            body.ActorID != 0)
        {
            return BadRequest("This endpoint only verifies a new organization contact email.");
        }

        string normalizedEmail;

        try
        {
            normalizedEmail = EmailAddressNormalizer.Normalize(body.EmailAddress);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }

        try
        {
            var challenge = await _codeStore.CreateCodeAsync(
                TwoFactorPurpose.NewOrganizationAdminEmail,
                0,
                normalizedEmail,
                request.HttpContext.RequestAborted);

            try
            {
                await _emailSender.SendCodeAsync(
                    normalizedEmail,
                    challenge.Code,
                    VerificationEmailKind.EmailAddressVerification,
                    request.HttpContext.RequestAborted);
            }
            catch
            {
                await _codeStore.RevokeCodeAsync(
                    challenge.VerificationID,
                    request.HttpContext.RequestAborted);

                throw;
            }

            return new OkObjectResult(
                new StartEmailVerificationResponseDto
                {
                    VerificationID = challenge.VerificationID,
                    ExpiresAtUtc = challenge.ExpiresAtUtc,
                    Message = "We sent a 6-digit verification code to that email address."
                });
        }
        catch (TwoFactorCodeRateLimitException exception)
        {
            request.HttpContext.Response.Headers["Retry-After"] =
                Math.Ceiling(exception.RetryAfter.TotalSeconds).ToString();

            return new ObjectResult(new { message = exception.Message })
            {
                StatusCode = StatusCodes.Status429TooManyRequests
            };
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Unable to start public organization email verification.");

            return ServerError("Unable to send the verification code.");
        }
    }

    [Function("VerifyPublicOrganizationEmail")]
    public async Task<IActionResult> VerifyEmailAsync(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "post",
            Route = "public/onboarding/email-verification/verify")]
        HttpRequest request)
    {
        VerifyEmailAddressRequestDto? body;

        try
        {
            body = await DeserializeAsync<VerifyEmailAddressRequestDto>(request);
        }
        catch (JsonException)
        {
            return BadRequest("Invalid email verification request.");
        }

        if (body is null ||
            body.Purpose != TwoFactorPurpose.NewOrganizationAdminEmail ||
            body.ActorID != 0 ||
            string.IsNullOrWhiteSpace(body.EmailAddress) ||
            string.IsNullOrWhiteSpace(body.VerificationID) ||
            string.IsNullOrWhiteSpace(body.Code))
        {
            return BadRequest("Email address, verification ID, and code are required.");
        }

        try
        {
            var verified = await _codeStore.VerifyCodeAsync(
                body.VerificationID,
                TwoFactorPurpose.NewOrganizationAdminEmail,
                0,
                body.EmailAddress,
                body.Code,
                consumeOnSuccess: false,
                cancellationToken: request.HttpContext.RequestAborted);

            if (!verified)
            {
                return new UnauthorizedObjectResult(new
                {
                    message = "The verification code is invalid, expired, or has already been used."
                });
            }

            return new OkObjectResult(
                new VerifyEmailAddressResponseDto
                {
                    VerificationToken = body.VerificationID,
                    Message = "Email address verified."
                });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Unable to verify a public organization email address.");

            return ServerError("Unable to verify the email address.");
        }
    }

    [Function("SubmitPublicOrganizationRequest")]
    public async Task<IActionResult> SubmitOrganizationAsync(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "post",
            Route = "public/onboarding/organization")]
        HttpRequest request)
    {
        PublicOrganizationRequestDto? body;

        try
        {
            body = await DeserializeAsync<PublicOrganizationRequestDto>(request);
        }
        catch (JsonException)
        {
            return BadRequest("Invalid organization request.");
        }

        var validationMessage = PublicOrganizationRequestValidator.Validate(body);
        if (!string.IsNullOrWhiteSpace(validationMessage))
        {
            return BadRequest(validationMessage);
        }

        var normalizedEmail = EmailAddressNormalizer.Normalize(body!.AdminEmail);
        var provinceStateCode = ProvinceStateCodeNormalizer.Normalize(body.ProvinceStateCode)!;

        try
        {
            var verified = await _codeStore.ConsumeVerifiedCodeAsync(
                body.AdminEmailVerificationToken!,
                TwoFactorPurpose.NewOrganizationAdminEmail,
                0,
                normalizedEmail,
                request.HttpContext.RequestAborted);

            if (!verified)
            {
                return new UnauthorizedObjectResult(new
                {
                    message = "Please verify the contact email address again before submitting."
                });
            }

            body.AdminEmail = normalizedEmail;
            body.ProvinceStateCode = provinceStateCode;

            var organizationID = await _repository.CreateAsync(
                body,
                request.HttpContext.RequestAborted);

            _logger.LogInformation(
                "Public onboarding request created EMAOrganizationID {EMAOrganizationID} with status {OrganizationStatusID}.",
                organizationID,
                body.OrganizationStatusID);

            return new ObjectResult(
                new PublicOrganizationRequestCreatedDto
                {
                    EMAOrganizationID = organizationID,
                    Message = "Your request has been submitted."
                })
            {
                StatusCode = StatusCodes.Status201Created
            };
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Unable to create a public organization onboarding request.");

            return ServerError("The request could not be submitted. Please try again.");
        }
    }

    private static async Task<T?> DeserializeAsync<T>(HttpRequest request)
    {
        return await JsonSerializer.DeserializeAsync<T>(
            request.Body,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            },
            request.HttpContext.RequestAborted);
    }

    private static BadRequestObjectResult BadRequest(string message) =>
        new(new { message });

    private static ObjectResult ServerError(string message) =>
        new(new { message })
        {
            StatusCode = StatusCodes.Status500InternalServerError
        };
}
