using System.Data;
using System.Text.Json;
using EMA.Api.DTOs;
using EMA.Api.Services.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Authentication;

public sealed class StartEmailVerification
{
    private readonly ILogger<StartEmailVerification> _logger;
    private readonly ITwoFactorCodeStore _codeStore;
    private readonly ITwoFactorEmailSender _emailSender;
    private readonly string _connectionString;

    public StartEmailVerification(
        ILogger<StartEmailVerification> logger,
        ITwoFactorCodeStore codeStore,
        ITwoFactorEmailSender emailSender)
    {
        _logger =
            logger;

        _codeStore =
            codeStore;

        _emailSender =
            emailSender;

        _connectionString =
            Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString.");
    }

    [Function("StartEmailVerification")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,             "post",
            Route = "email-verification/start")]
        HttpRequest request)
    {
        StartEmailVerificationRequestDto? startRequest;

        try
        {
            startRequest =
                await JsonSerializer
                    .DeserializeAsync<StartEmailVerificationRequestDto>(
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

        if (startRequest is null ||
            !EmailVerificationPurposeValidator
                .IsEmailAddressPurpose(
                    startRequest.Purpose) ||
            !EmailVerificationPurposeValidator
                .HasValidActor(
                    startRequest.Purpose,
                    startRequest.ActorID))
        {
            return new BadRequestObjectResult(new
            {
                message =
                    "A valid verification purpose and target are required."
            });
        }

        string normalizedEmail;

        try
        {
            normalizedEmail =
                EmailAddressNormalizer.Normalize(
                    startRequest.EmailAddress);
        }
        catch (ArgumentException exception)
        {
            return new BadRequestObjectResult(new
            {
                message =
                    exception.Message
            });
        }

        try
        {
            if (startRequest.ActorID > 0 &&
                !await OrganizationExistsAsync(
                    startRequest.ActorID,
                    request.HttpContext.RequestAborted))
            {
                return new NotFoundObjectResult(new
                {
                    message =
                        "The organization was not found."
                });
            }

            var challenge =
                await _codeStore.CreateCodeAsync(
                    startRequest.Purpose,
                    startRequest.ActorID,
                    normalizedEmail,
                    request.HttpContext.RequestAborted);

            try
            {
                await _emailSender.SendCodeAsync(
                    normalizedEmail,
                    challenge.Code,
                    VerificationEmailKind
                        .EmailAddressVerification,
                    request.HttpContext.RequestAborted);
            }
            catch
            {
                await _codeStore.RevokeCodeAsync(
                    challenge.VerificationID,
                    request.HttpContext.RequestAborted);

                throw;
            }

            _logger.LogInformation(
                "Email verification started. Purpose: {Purpose}; ActorID: {ActorID}.",
                startRequest.Purpose,
                startRequest.ActorID);

            return new OkObjectResult(
                new StartEmailVerificationResponseDto
                {
                    VerificationID =
                        challenge.VerificationID,

                    ExpiresAtUtc =
                        challenge.ExpiresAtUtc,

                    Message =
                        "We sent a 6-digit verification code to that email address."
                });
        }
        catch (TwoFactorCodeRateLimitException exception)
        {
            request.HttpContext
                .Response
                .Headers["Retry-After"] =
                    Math
                        .Ceiling(
                            exception
                                .RetryAfter
                                .TotalSeconds)
                        .ToString();

            return new ObjectResult(new
            {
                message =
                    exception.Message
            })
            {
                StatusCode =
                    StatusCodes.Status429TooManyRequests
            };
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Unable to start email verification. Purpose: {Purpose}; ActorID: {ActorID}.",
                startRequest.Purpose,
                startRequest.ActorID);

            return new ObjectResult(new
            {
                message =
                    "Unable to send the verification code."
            })
            {
                StatusCode =
                    StatusCodes.Status500InternalServerError
            };
        }
    }

    private async Task<bool> OrganizationExistsAsync(
        int EMAOrganizationID,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP (1)
                1
            FROM dbo.Organization
            WHERE EMAOrganizationID = @EMAOrganizationID;
            """;

        await using var connection =
            new SqlConnection(
                _connectionString);

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters
            .Add(
                "@EMAOrganizationID",
                SqlDbType.Int)
            .Value =
                EMAOrganizationID;

        return await command.ExecuteScalarAsync(
            cancellationToken) is not null;
    }
}
