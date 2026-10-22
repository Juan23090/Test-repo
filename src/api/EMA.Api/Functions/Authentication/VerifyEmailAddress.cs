using System.Text.Json;
using EMA.Api.DTOs;
using EMA.Api.Services.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Authentication;

public sealed class VerifyEmailAddress
{
    private readonly ILogger<VerifyEmailAddress> _logger;
    private readonly ITwoFactorCodeStore _codeStore;

    public VerifyEmailAddress(
        ILogger<VerifyEmailAddress> logger,
        ITwoFactorCodeStore codeStore)
    {
        _logger =
            logger;

        _codeStore =
            codeStore;
    }

    [Function("VerifyEmailAddress")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,             "post",
            Route = "email-verification/verify")]
        HttpRequest request)
    {
        VerifyEmailAddressRequestDto? verifyRequest;

        try
        {
            verifyRequest =
                await JsonSerializer
                    .DeserializeAsync<VerifyEmailAddressRequestDto>(
                        request.Body,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive =
                                true
                        },
                        request.HttpContext.RequestAborted);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult(new
            {
                message =
                    "Invalid email verification request."
            });
        }

        if (verifyRequest is null ||
            !EmailVerificationPurposeValidator
                .IsEmailAddressPurpose(
                    verifyRequest.Purpose) ||
            !EmailVerificationPurposeValidator
                .HasValidActor(
                    verifyRequest.Purpose,
                    verifyRequest.ActorID) ||
            string.IsNullOrWhiteSpace(
                verifyRequest.EmailAddress) ||
            string.IsNullOrWhiteSpace(
                verifyRequest.VerificationID) ||
            string.IsNullOrWhiteSpace(
                verifyRequest.Code))
        {
            return new BadRequestObjectResult(new
            {
                message =
                    "Email address, verification ID, and code are required."
            });
        }

        try
        {
            var verified =
                await _codeStore.VerifyCodeAsync(
                    verifyRequest.VerificationID,
                    verifyRequest.Purpose,
                    verifyRequest.ActorID,
                    verifyRequest.EmailAddress,
                    verifyRequest.Code,
                    consumeOnSuccess:
                        false,
                    cancellationToken:
                        request.HttpContext.RequestAborted);

            if (!verified)
            {
                return new UnauthorizedObjectResult(new
                {
                    message =
                        "The verification code is invalid, expired, or has already been used."
                });
            }

            _logger.LogInformation(
                "Email address verified. Purpose: {Purpose}; ActorID: {ActorID}.",
                verifyRequest.Purpose,
                verifyRequest.ActorID);

            return new OkObjectResult(
                new VerifyEmailAddressResponseDto
                {
                    VerificationToken =
                        verifyRequest.VerificationID,

                    Message =
                        "Email address verified."
                });
        }
        catch (ArgumentException exception)
        {
            return new BadRequestObjectResult(new
            {
                message =
                    exception.Message
            });
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Unable to verify email address.");

            return new ObjectResult(new
            {
                message =
                    "Unable to verify the email address."
            })
            {
                StatusCode =
                    StatusCodes.Status500InternalServerError
            };
        }
    }
}
