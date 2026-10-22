using System.Text.Json;
using EMA.Api.DTOs;
using EMA.Api.Services.Authentication;
using EMA.Api.Services.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Authentication;

public sealed class CompleteUnifiedLogin
{
    private readonly ILogger<CompleteUnifiedLogin>
        _logger;

    private readonly string _connectionString;
    private readonly ITwoFactorCodeStore
        _twoFactorCodeStore;
    private readonly IApiSessionService
        _apiSessionService;
    private readonly ITrustedDeviceService
        _trustedDeviceService;
    private readonly int _platformOrganizationID;

    public CompleteUnifiedLogin(
        ILogger<CompleteUnifiedLogin> logger,
        ITwoFactorCodeStore twoFactorCodeStore,
        IApiSessionService apiSessionService,
        ITrustedDeviceService trustedDeviceService,
        ApiRequestBoundaryValidator authorization)
    {
        _logger =
            logger;

        _twoFactorCodeStore =
            twoFactorCodeStore;

        _apiSessionService =
            apiSessionService;

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

    [Function("CompleteUnifiedLogin")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "post",
            Route = "login/unified-complete")]
        HttpRequest request)
    {
        SecureCompleteUnifiedLoginRequestDto?
            completeRequest;

        try
        {
            completeRequest =
                await JsonSerializer
                    .DeserializeAsync<
                        SecureCompleteUnifiedLoginRequestDto>(
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

        if (completeRequest is null ||
            completeRequest.EMAOrganizationID <= 0 ||
            (
                completeRequest.UseTrustedDevice
                    ? !string.IsNullOrWhiteSpace(
                        completeRequest.VerificationID)
                    : !IsValidVerificationID(
                        completeRequest.VerificationID)
            ) ||
            !LoginIdentifierNormalizer
                .TryNormalize(
                    completeRequest.Identifier,
                    out var identifier,
                    out _) ||
            !Enum.TryParse<ApiActorType>(
                completeRequest.ActorType,
                ignoreCase: false,
                out var actorType) ||
            !Enum.IsDefined(
                actorType) ||
            !IsValidSelectedActor(
                completeRequest,
                actorType))
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

            var selectedCandidates =
                candidates
                    .Where(
                        candidate =>
                            IsSelectedCandidate(
                                candidate,
                                completeRequest,
                                actorType))
                    .ToList();

            if (selectedCandidates.Count == 0)
            {
                return InvalidSelection();
            }

            string? authenticatedEmail =
                null;

            if (completeRequest.UseTrustedDevice)
            {
                var selectedEmails =
                    selectedCandidates
                        .Select(
                            candidate =>
                                candidate.EmailAddress)
                        .Distinct(
                            StringComparer
                                .OrdinalIgnoreCase)
                        .ToList();

                if (selectedEmails.Count != 1 ||
                    !await _trustedDeviceService
                        .IsTrustedAsync(
                            selectedEmails[0],
                            request,
                            request.HttpContext
                                .RequestAborted))
                {
                    return InvalidSelection();
                }

                authenticatedEmail =
                    selectedEmails[0];
            }
            else
            {
                foreach (var emailAddress in
                         selectedCandidates
                             .Select(
                                 candidate =>
                                     candidate.EmailAddress)
                             .Distinct(
                                 StringComparer
                                     .OrdinalIgnoreCase))
                {
                    var wasConsumed =
                        await _twoFactorCodeStore
                            .ConsumeVerifiedCodeAsync(
                                completeRequest
                                    .VerificationID,
                                TwoFactorPurpose
                                    .UnifiedLogin,
                                UnifiedLoginCandidateQueries
                                    .CalculateChallengeActorID(
                                        identifier,
                                        emailAddress),
                                emailAddress,
                                request.HttpContext
                                    .RequestAborted);

                    if (!wasConsumed)
                    {
                        continue;
                    }

                    authenticatedEmail =
                        emailAddress;

                    break;
                }

                if (authenticatedEmail is null)
                {
                    return InvalidSelection();
                }
            }

            LoginResponseDto? loginResponse;

            if (actorType ==
                ApiActorType.OrganizationUser)
            {
                loginResponse =
                    await VerifyOrganizationLoginCode
                        .BuildLoginResponseAsync(
                            connection,
                            completeRequest
                                .EMAOrganizationID,
                            completeRequest
                                .UserID!.Value);

                if (loginResponse is not null)
                {
                    await VerifyOrganizationLoginCode
                        .MarkEmailAddressValidatedAsync(
                            connection,
                            loginResponse.UserID,
                            request.HttpContext
                                .RequestAborted);
                }
            }
            else
            {
                loginResponse =
                    await VerifyDonorLoginCode
                        .BuildDonorLoginResponseAsync(
                            connection,
                            completeRequest
                                .EMAOrganizationID,
                            completeRequest
                                .DonorID!.Value,
                            _platformOrganizationID);
            }

            if (loginResponse is null)
            {
                return InvalidSelection();
            }

            var accessToken =
                await _apiSessionService
                    .CreateSessionAsync(
                        loginResponse,
                        request.HttpContext
                            .RequestAborted);

            loginResponse.AccessToken =
                accessToken.Token;

            loginResponse
                .AccessTokenExpiresAtUtc =
                    accessToken.ExpiresAtUtc;

            if (completeRequest.TrustThisDevice &&
                !completeRequest.UseTrustedDevice)
            {
                await _trustedDeviceService
                    .RegisterSuccessfulAuthenticationAsync(
                        authenticatedEmail,
                        request,
                        request.HttpContext
                            .RequestAborted);
            }

            _logger.LogInformation(
                "Unified login completed for {ActorType}, UserID {UserID}, DonorID {DonorID}, EMAOrganizationID {EMAOrganizationID}.",
                actorType,
                loginResponse.UserID,
                loginResponse.DonorID,
                loginResponse
                    .EMAOrganizationID);

            return new OkObjectResult(
                loginResponse);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error completing unified login.");

            return new ObjectResult(
                new
                {
                    message =
                        "Unable to complete login."
                })
            {
                StatusCode =
                    StatusCodes
                        .Status500InternalServerError
            };
        }
    }

    private static bool IsSelectedCandidate(
        UnifiedLoginCandidate candidate,
        SecureCompleteUnifiedLoginRequestDto request,
        ApiActorType actorType)
    {
        if (candidate.EMAOrganizationID !=
            request.EMAOrganizationID)
        {
            return false;
        }

        return actorType switch
        {
            ApiActorType.OrganizationUser =>
                candidate.Kind ==
                UnifiedLoginCandidateKind
                    .OrganizationUser &&
                candidate.UserID ==
                request.UserID,

            ApiActorType.Donor =>
                candidate.Kind ==
                UnifiedLoginCandidateKind
                    .Donor &&
                candidate.DonorID ==
                request.DonorID,

            _ =>
                false
        };
    }

    private static bool IsValidSelectedActor(
        SecureCompleteUnifiedLoginRequestDto request,
        ApiActorType actorType)
    {
        return actorType switch
        {
            ApiActorType.OrganizationUser =>
                request.UserID > 0 &&
                !request.DonorID.HasValue,

            ApiActorType.Donor =>
                request.DonorID > 0 &&
                !request.UserID.HasValue,

            _ =>
                false
        };
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

    private static BadRequestObjectResult
        InvalidRequest()
    {
        return new BadRequestObjectResult(
            new
            {
                message =
                    "A valid verified login selection is required."
            });
    }

    private static UnauthorizedObjectResult
        InvalidSelection()
    {
        return new UnauthorizedObjectResult(
            new
            {
                message =
                    "The verified login selection is invalid or expired."
            });
    }
}
