using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using EMA.Api.DTOs;
using EMA.Api.Services.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Authentication;

public sealed class UnifiedLoginLookup
{
    private const int PublicChallengeCount = 5;

    private const string VerificationMessage =
        "If that phone number or email address matches an active account, a 6-digit code has been sent to the email address on file.";

    private readonly ILogger<UnifiedLoginLookup> _logger;
    private readonly string _connectionString;
    private readonly ITwoFactorCodeStore _twoFactorCodeStore;
    private readonly ITwoFactorEmailSender _twoFactorEmailSender;
    private readonly ITrustedDeviceService _trustedDeviceService;
    private readonly int _platformOrganizationID;

    public UnifiedLoginLookup(
        ILogger<UnifiedLoginLookup> logger,
        ITwoFactorCodeStore twoFactorCodeStore,
        ITwoFactorEmailSender twoFactorEmailSender,
        ITrustedDeviceService trustedDeviceService,
        ApiRequestBoundaryValidator authorization)
    {
        _logger =
            logger;

        _twoFactorCodeStore =
            twoFactorCodeStore;

        _twoFactorEmailSender =
            twoFactorEmailSender;

        _trustedDeviceService =
            trustedDeviceService;

        _platformOrganizationID =
            authorization.PlatformOrganizationID;

        _connectionString =
            Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString.");
    }

    [Function("UnifiedLoginLookup")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "post",
            Route = "login/unified-lookup")]
        HttpRequest request)
    {
        var startedAt =
            Stopwatch.GetTimestamp();

        SecureUnifiedLoginLookupRequestDto?
            lookupRequest;

        try
        {
            lookupRequest =
                await JsonSerializer
                    .DeserializeAsync<
                        SecureUnifiedLoginLookupRequestDto>(
                        request.Body,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive =
                                true
                        },
                        request.HttpContext
                            .RequestAborted);
        }
        catch (JsonException)
        {
            return InvalidRequest();
        }

        if (!LoginIdentifierNormalizer
                .TryNormalize(
                    lookupRequest?.Identifier,
                    out var identifier,
                    out var validationMessage))
        {
            return new BadRequestObjectResult(
                new
                {
                    message =
                        validationMessage
                });
        }

        try
        {
            await using var connection =
                new SqlConnection(
                    _connectionString);

            await connection.OpenAsync(
                request.HttpContext
                    .RequestAborted);

            var candidates =
                await UnifiedLoginCandidateQueries
                    .GetCandidatesAsync(
                        connection,
                        identifier,
                        _platformOrganizationID,
                        request.HttpContext
                            .RequestAborted);

            var destinationEmails =
                candidates
                    .Select(
                        candidate =>
                            candidate.EmailAddress)
                    .Distinct(
                        StringComparer
                            .OrdinalIgnoreCase)
                    .OrderBy(
                        emailAddress =>
                            emailAddress,
                        StringComparer
                            .OrdinalIgnoreCase)
                    .Take(
                        PublicChallengeCount)
                    .ToList();

            if (destinationEmails.Count == 1 &&
                await _trustedDeviceService
                    .IsTrustedAsync(
                        destinationEmails[0],
                        request,
                        request.HttpContext
                            .RequestAborted))
            {
                var trustedEmail =
                    destinationEmails[0];

                var users =
                    UnifiedLoginCandidateQueries
                        .GetUserChoices(
                            candidates,
                            trustedEmail);

                var donors =
                    UnifiedLoginCandidateQueries
                        .GetDonorChoices(
                            candidates,
                            trustedEmail);

                if (users.Count > 0 ||
                    donors.Count > 0)
                {
                    await DelaySuccessfulResponseAsync(
                        startedAt,
                        request.HttpContext
                            .RequestAborted);

                    return new OkObjectResult(
                        new SecureLoginVerificationStartResponseDto
                        {
                            RequiresVerification =
                                false,

                            Message =
                                "Trusted device recognized.",

                            TrustedLogin =
                                new SecureUnifiedLoginLookupResponseDto
                                {
                                    IsTrustedDeviceLogin =
                                        true,

                                    Users =
                                        users,

                                    Donors =
                                        donors
                                }
                        });
                }
            }

            var verificationIDs =
                new List<string>(
                    PublicChallengeCount);

            foreach (var emailAddress in
                     destinationEmails)
            {
                var verificationID =
                    await TrySendVerificationCodeAsync(
                        identifier,
                        emailAddress,
                        request.HttpContext
                            .RequestAborted);

                if (!string.IsNullOrWhiteSpace(
                        verificationID))
                {
                    verificationIDs.Add(
                        verificationID);
                }
            }

            while (verificationIDs.Count <
                   PublicChallengeCount)
            {
                verificationIDs.Add(
                    Guid.NewGuid()
                        .ToString("N"));
            }


            _logger.LogInformation(
                "Unified login verification request evaluated for {IdentifierKind} with {CandidateCount} candidate(s) across {DestinationCount} destination(s).",
                identifier.Kind,
                candidates.Count,
                destinationEmails.Count);

            await DelaySuccessfulResponseAsync(
                startedAt,
                request.HttpContext
                    .RequestAborted);

            return new OkObjectResult(
                new SecureLoginVerificationStartResponseDto
                {
                    VerificationIDs =
                        verificationIDs,

                    Message =
                        VerificationMessage
                });
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error starting unified login verification.");

            return new ObjectResult(
                new
                {
                    message =
                        "Unable to start login verification."
                })
            {
                StatusCode =
                    StatusCodes
                        .Status500InternalServerError
            };
        }
    }

    private async Task<string?> TrySendVerificationCodeAsync(
        NormalizedLoginIdentifier identifier,
        string normalizedEmail,
        CancellationToken cancellationToken)
    {
        TwoFactorCodeRecord? verificationRecord =
            null;

        try
        {
            verificationRecord =
                await _twoFactorCodeStore
                    .CreateCodeAsync(
                        TwoFactorPurpose.UnifiedLogin,
                        UnifiedLoginCandidateQueries
                            .CalculateChallengeActorID(
                                identifier,
                                normalizedEmail),
                        normalizedEmail,
                        cancellationToken);

            await _twoFactorEmailSender
                .SendCodeAsync(
                    normalizedEmail,
                    verificationRecord.Code,
                    VerificationEmailKind.Login,
                    cancellationToken);

            return verificationRecord
                .VerificationID;
        }
        catch (TwoFactorCodeRateLimitException exception)
        {
            _logger.LogWarning(
                "Unified login verification was rate limited for {RetryAfterSeconds} seconds.",
                Math.Ceiling(
                    exception
                        .RetryAfter
                        .TotalSeconds));
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Unable to deliver a unified login verification code.");
        }

        if (verificationRecord is not null)
        {
            await _twoFactorCodeStore
                .RevokeCodeAsync(
                    verificationRecord
                        .VerificationID,
                    cancellationToken);
        }

        return null;
    }

    private static BadRequestObjectResult
        InvalidRequest()
    {
        return new BadRequestObjectResult(
            new
            {
                message =
                    "A phone number or email address is required."
            });
    }

    private static async Task
        DelaySuccessfulResponseAsync(
            long startedAt,
            CancellationToken cancellationToken)
    {
        var targetDuration =
            TimeSpan.FromMilliseconds(
                RandomNumberGenerator
                    .GetInt32(
                        650,
                        851));

        var remaining =
            targetDuration -
            Stopwatch.GetElapsedTime(
                startedAt);

        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(
                remaining,
                cancellationToken);
        }
    }
}
