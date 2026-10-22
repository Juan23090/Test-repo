using System.Text.Json;
using EMA.Api.DTOs;
using EMA.Api.Services.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Authentication;

public sealed class VerifyUnifiedLoginCode
{
    private const int MaximumChallengeCount = 5;

    private readonly ILogger<VerifyUnifiedLoginCode>
        _logger;

    private readonly string _connectionString;
    private readonly ITwoFactorCodeStore
        _twoFactorCodeStore;
    private readonly int _platformOrganizationID;

    public VerifyUnifiedLoginCode(
        ILogger<VerifyUnifiedLoginCode> logger,
        ITwoFactorCodeStore twoFactorCodeStore,
        ApiRequestBoundaryValidator authorization)
    {
        _logger =
            logger;

        _twoFactorCodeStore =
            twoFactorCodeStore;

        _platformOrganizationID =
            authorization.PlatformOrganizationID;

        _connectionString =
            Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString.");
    }

    [Function("VerifyUnifiedLoginCode")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "post",
            Route = "login/unified-verify")]
        HttpRequest request)
    {
        SecureVerifyUnifiedLoginCodeRequestDto?
            verifyRequest;

        try
        {
            verifyRequest =
                await JsonSerializer
                    .DeserializeAsync<
                        SecureVerifyUnifiedLoginCodeRequestDto>(
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

        if (verifyRequest is null ||
            !LoginIdentifierNormalizer
                .TryNormalize(
                    verifyRequest.Identifier,
                    out var identifier,
                    out _) ||
            !TryGetCode(
                verifyRequest.Code,
                out var code))
        {
            return InvalidRequest();
        }

        var verificationIDs =
            (verifyRequest
                 .VerificationIDs
             ?? new List<string>())
                .Where(
                    IsValidVerificationID)
                .Distinct(
                    StringComparer.Ordinal)
                .Take(
                    MaximumChallengeCount)
                .ToList();

        if (verificationIDs.Count == 0)
        {
            return InvalidRequest();
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

            string? verifiedEmail =
                null;

            string? verifiedVerificationID =
                null;

            foreach (var emailAddress in
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
                             MaximumChallengeCount))
            {
                foreach (var verificationID in
                         verificationIDs)
                {
                    var isVerified =
                        await _twoFactorCodeStore
                            .VerifyCodeAsync(
                                verificationID,
                                TwoFactorPurpose
                                    .UnifiedLogin,
                                UnifiedLoginCandidateQueries
                                    .CalculateChallengeActorID(
                                        identifier,
                                        emailAddress),
                                emailAddress,
                                code,
                                consumeOnSuccess:
                                    false,
                                cancellationToken:
                                    request.HttpContext
                                        .RequestAborted);

                    if (!isVerified)
                    {
                        continue;
                    }

                    verifiedEmail =
                        emailAddress;

                    verifiedVerificationID =
                        verificationID;

                    break;
                }

                if (verifiedEmail is not null)
                {
                    break;
                }
            }

            if (verifiedEmail is null ||
                verifiedVerificationID is null)
            {
                return InvalidCode();
            }

            var users =
                UnifiedLoginCandidateQueries
                    .GetUserChoices(
                        candidates,
                        verifiedEmail);

            var donors =
                UnifiedLoginCandidateQueries
                    .GetDonorChoices(
                        candidates,
                        verifiedEmail);

            if (users.Count == 0 &&
                donors.Count == 0)
            {
                return InvalidCode();
            }

            _logger.LogInformation(
                "Unified login code verified with {UserCount} user choice(s) and {DonorCount} donor choice(s).",
                users.Count,
                donors.Count);

            return new OkObjectResult(
                new SecureUnifiedLoginLookupResponseDto
                {
                    VerificationID =
                        verifiedVerificationID,

                    Users =
                        users,

                    Donors =
                        donors
                });
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error verifying unified login code.");

            return new ObjectResult(
                new
                {
                    message =
                        "Unable to verify login code."
                })
            {
                StatusCode =
                    StatusCodes
                        .Status500InternalServerError
            };
        }
    }

    private static BadRequestObjectResult
        InvalidRequest()
    {
        return new BadRequestObjectResult(
            new
            {
                message =
                    "A valid identifier, verification challenge, and 6-digit code are required."
            });
    }

    private static UnauthorizedObjectResult
        InvalidCode()
    {
        return new UnauthorizedObjectResult(
            new
            {
                message =
                    "The verification code is invalid or expired."
            });
    }

    private static bool IsValidVerificationID(
        string? verificationID)
    {
        return !string.IsNullOrWhiteSpace(
                   verificationID) &&
               verificationID.Length == 32 &&
               Guid.TryParseExact(
                   verificationID,
                   "N",
                   out _);
    }

    private static bool TryGetCode(
        string? suppliedCode,
        out string code)
    {
        code =
            new string(
                (suppliedCode
                 ?? string.Empty)
                .Where(char.IsDigit)
                .ToArray());

        return code.Length == 6;
    }
}
