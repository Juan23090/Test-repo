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

public sealed class StartOrganizationLoginVerification
{
    private const string VerificationMessage =
        "We have sent a 6-digit code to the email address on file.";

    private readonly ILogger<StartOrganizationLoginVerification> _logger;
    private readonly string _connectionString;
    private readonly ITwoFactorCodeStore _twoFactorCodeStore;
    private readonly ITwoFactorEmailSender _twoFactorEmailSender;

    public StartOrganizationLoginVerification(
        ILogger<StartOrganizationLoginVerification> logger,
        ITwoFactorCodeStore twoFactorCodeStore,
        ITwoFactorEmailSender twoFactorEmailSender)
    {
        _logger =
            logger;

        _twoFactorCodeStore =
            twoFactorCodeStore;

        _twoFactorEmailSender =
            twoFactorEmailSender;

        _connectionString =
            Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString.");
    }

    [Function("StartOrganizationLoginVerification")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,             "post",
            Route = "login/start-verification")]
        HttpRequest request)
    {
        StartOrganizationLoginVerificationRequestDto? startRequest;

        try
        {
            startRequest =
                await JsonSerializer.DeserializeAsync<StartOrganizationLoginVerificationRequestDto>(
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
                    "Invalid login verification request."
            });
        }

        if (startRequest is null ||
            startRequest.UserID <= 0 ||
            startRequest.EMAOrganizationID <= 0)
        {
            return new BadRequestObjectResult(new
            {
                message =
                    "User and organization are required."
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
                    ou.OrganizationUserID,
                    u.EmailAddress
                FROM dbo.[User] u
                INNER JOIN dbo.OrganizationUser ou
                    ON u.UserID =
                       ou.UserID
                WHERE u.UserID = @UserID
                  AND ou.EMAOrganizationID = @EMAOrganizationID
                  AND ou.OrganizationUserStatusID = 10
                  AND u.IsActive = 1
                  AND u.SandboxOrganizationID IS NULL
                  AND EXISTS
                  (
                      SELECT 1
                      FROM dbo.OrganizationUserRole
                          organizationUserRole
                      INNER JOIN dbo.[Role] role
                          ON role.RoleID =
                             organizationUserRole.RoleID
                      WHERE organizationUserRole
                                .EMAOrganizationID =
                            ou.EMAOrganizationID
                        AND organizationUserRole.UserID =
                            ou.UserID
                        AND ISNULL(
                                role.IsActive,
                                'Y') = 'Y'
                  );
                """;

            await using var command =
                new SqlCommand(
                    sql,
                    connection);

            command.Parameters.Add(
                "@UserID",
                SqlDbType.Int)
                .Value = startRequest.UserID;

            command.Parameters.Add(
                "@EMAOrganizationID",
                SqlDbType.Int)
                .Value = startRequest.EMAOrganizationID;

            int organizationUserID;
            string? emailAddress;

            await using (var reader =
                         await command
                             .ExecuteReaderAsync(
                                 request.HttpContext
                                     .RequestAborted))
            {
                if (!await reader.ReadAsync(
                        request.HttpContext
                            .RequestAborted))
                {
                    return new UnauthorizedObjectResult(
                        new
                        {
                            message =
                                "Login verification could not be started."
                        });
                }

                organizationUserID =
                    Convert.ToInt32(
                        reader[
                            "OrganizationUserID"]);

                emailAddress =
                    reader["EmailAddress"]
                        ?.ToString();
            }

            if (string.IsNullOrWhiteSpace(
                    emailAddress))
            {
                return new UnauthorizedObjectResult(
                    new
                    {
                        message =
                            "Login verification could not be started."
                    });
            }

            var verificationRecord =
                await _twoFactorCodeStore.CreateCodeAsync(
                    TwoFactorPurpose.OrganizationLogin,
                    organizationUserID,
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
                "Organization login verification started for UserID {UserID}, EMAOrganizationID {EMAOrganizationID}.",
                startRequest.UserID,
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
                "Error starting organization login verification.");

            return new ObjectResult(new
            {
                message =
                    "Unable to start login verification."
            })
            {
                StatusCode =
                    StatusCodes.Status500InternalServerError
            };
        }
    }
}
