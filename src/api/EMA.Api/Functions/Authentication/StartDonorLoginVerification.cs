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

public sealed class StartDonorLoginVerification
{
    private const string VerificationMessage =
        "We have sent a 6-digit code to the email address on file.";

    private readonly ILogger<StartDonorLoginVerification> _logger;
    private readonly string _connectionString;
    private readonly ITwoFactorCodeStore _twoFactorCodeStore;
    private readonly ITwoFactorEmailSender _twoFactorEmailSender;
    private readonly int _platformOrganizationID;

    public StartDonorLoginVerification(
        ILogger<StartDonorLoginVerification> logger,
        ITwoFactorCodeStore twoFactorCodeStore,
        ITwoFactorEmailSender twoFactorEmailSender,
        ApiRequestBoundaryValidator authorization)
    {
        _logger =
            logger;

        _twoFactorCodeStore =
            twoFactorCodeStore;

        _twoFactorEmailSender =
            twoFactorEmailSender;

        _platformOrganizationID =
            authorization
                .PlatformOrganizationID;

        _connectionString =
            Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString.");
    }

    [Function("StartDonorLoginVerification")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,             "post",
            Route = "donor-login/start-verification")]
        HttpRequest request)
    {
        StartDonorLoginVerificationRequestDto? startRequest;

        try
        {
            startRequest =
                await JsonSerializer.DeserializeAsync<StartDonorLoginVerificationRequestDto>(
                    request.Body,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult(new
            {
                message =
                    "Invalid donor verification request."
            });
        }

        if (startRequest is null ||
            startRequest.DonorID <= 0 ||
            startRequest.EMAOrganizationID <= 0)
        {
            return new BadRequestObjectResult(new
            {
                message =
                    "Donor and organization are required."
            });
        }

        try
        {
            await using var connection =
                new SqlConnection(
                    _connectionString);

            await connection.OpenAsync();

            const string sql = """
                SELECT
                    d.EmailAddress
                FROM dbo.Donor d
                INNER JOIN dbo.Organization organization
                    ON organization.EMAOrganizationID =
                       d.EMAOrganizationID
                INNER JOIN dbo.[Role] donorRole
                    ON donorRole.RoleID =
                       organization.DonorRoleID
                WHERE d.EMADonorID = @DonorID
                  AND d.EMAOrganizationID = @EMAOrganizationID
                  AND organization.EMAOrganizationID <>
                      @PlatformOrganizationID
                  AND ISNULL(
                          organization.OrganizationStatusID,
                          -1) <> 0
                  AND ISNULL(
                          donorRole.IsActive,
                          'Y') = 'Y';
                """;

            await using var command =
                new SqlCommand(
                    sql,
                    connection);

            command.Parameters.Add(
                "@DonorID",
                SqlDbType.Int)
                .Value = startRequest.DonorID;

            command.Parameters.Add(
                "@EMAOrganizationID",
                SqlDbType.Int)
                .Value = startRequest.EMAOrganizationID;

            command.Parameters.Add(
                "@PlatformOrganizationID",
                SqlDbType.Int)
                .Value =
                    _platformOrganizationID;

            var emailAddress =
                await command.ExecuteScalarAsync()
                as string;

            if (string.IsNullOrWhiteSpace(
                    emailAddress))
            {
                return new ObjectResult(new
                {
                    message =
                        "This donor does not have an email address on file for login verification."
                })
                {
                    StatusCode =
                        StatusCodes.Status409Conflict
                };
            }

            var verificationRecord =
                await _twoFactorCodeStore.CreateCodeAsync(
                    TwoFactorPurpose.DonorLogin,
                    startRequest.DonorID,
                    emailAddress,
                    request.HttpContext.RequestAborted);

            try
            {
                await _twoFactorEmailSender.SendCodeAsync(
                    emailAddress,
                    verificationRecord.Code,
                    VerificationEmailKind.Login,
                    request.HttpContext.RequestAborted);
            }
            catch
            {
                await _twoFactorCodeStore.RevokeCodeAsync(
                    verificationRecord.VerificationID,
                    request.HttpContext.RequestAborted);

                throw;
            }
            _logger.LogInformation(
                "Donor login verification started for DonorID {DonorID}, EMAOrganizationID {EMAOrganizationID}.",
                startRequest.DonorID,
                startRequest.EMAOrganizationID);

            return new OkObjectResult(
                new LoginVerificationStartResponseDto
                {
                    VerificationID =
                        verificationRecord.VerificationID,

                    Message =
                        VerificationMessage
                });
        }
        catch (TwoFactorCodeRateLimitException
               exception)
        {
            request.HttpContext
                .Response
                .Headers["Retry-After"] =
                    Math.Ceiling(
                            exception
                                .RetryAfter
                                .TotalSeconds)
                        .ToString();

            return new ObjectResult(
                new
                {
                    message =
                        exception.Message
                })
            {
                StatusCode =
                    StatusCodes
                        .Status429TooManyRequests
            };
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error starting donor login verification.");

            return new ObjectResult(new
            {
                message =
                    "Unable to start donor login verification."
            })
            {
                StatusCode =
                    StatusCodes.Status500InternalServerError
            };
        }
    }
}
