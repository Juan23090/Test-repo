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

/// <summary>
/// Finds one active user by 10-digit phone number,
/// then starts email verification.
/// </summary>
public sealed class LookupLoginUser
{
    private const string VerificationMessage =
        "We have sent a 6-digit code to the email address on file.";

    private readonly ILogger<LookupLoginUser> _logger;
    private readonly string _connectionString;
    private readonly ITwoFactorCodeStore _twoFactorCodeStore;
    private readonly ITwoFactorEmailSender _twoFactorEmailSender;

    public LookupLoginUser(
        ILogger<LookupLoginUser> logger,
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

    [Function("LookupLoginUser")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,             "post",
            Route = "login/lookup")]
        HttpRequest request)
    {
        if (!IsPasswordlessLoginEnabled())
        {
            return new ObjectResult(new
            {
                message =
                    "Passwordless login is disabled."
            })
            {
                StatusCode =
                    StatusCodes.Status403Forbidden
            };
        }

        LoginLookupRequestDto? lookupRequest;

        try
        {
            lookupRequest =
                await JsonSerializer.DeserializeAsync<LoginLookupRequestDto>(
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
                    "Invalid lookup request."
            });
        }

        var identifier =
            lookupRequest
                ?.Identifier
                ?.Trim()
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(
                identifier))
        {
            return new BadRequestObjectResult(new
            {
                message =
                    "Please enter your 10 digit phone number."
            });
        }

        if (identifier.Contains(
                '@',
                StringComparison.Ordinal))
        {
            return new BadRequestObjectResult(new
            {
                message =
                    "Please enter your 10 digit phone number."
            });
        }

        var phoneDigits =
            DigitsOnly(
                identifier);

        if (phoneDigits.Length != 10)
        {
            return new BadRequestObjectResult(new
            {
                message =
                    "Please enter a valid 10 digit phone number."
            });
        }

        try
        {
            await using var connection =
                new SqlConnection(
                    _connectionString);

            await connection.OpenAsync();

            const string sql = """
                SELECT DISTINCT
                    u.UserID,
                    u.UserName,
                    u.EmailAddress,
                    u.PhoneNumber,
                    o.EMAOrganizationID,
                    o.OrganizationName
                FROM [dbo].[User] u
                INNER JOIN [dbo].[OrganizationUser] ou
                    ON u.UserID =
                       ou.UserID
                INNER JOIN [dbo].[Organization] o
                    ON ou.EMAOrganizationID =
                       o.EMAOrganizationID
                WHERE u.IsActive = 1
                  AND RIGHT(
                      REPLACE(
                          REPLACE(
                              REPLACE(
                                  REPLACE(
                                      REPLACE(
                                          REPLACE(
                                              ISNULL(
                                                  u.PhoneNumber,
                                                  ''
                                              ),
                                              '(',
                                              ''
                                          ),
                                          ')',
                                          ''
                                      ),
                                      '-',
                                      ''
                                  ),
                                  ' ',
                                  ''
                              ),
                              '.',
                              ''
                          ),
                          '+',
                          ''
                      ),
                      10
                  ) = @PhoneDigits
                ORDER BY
                    u.UserID,
                    o.OrganizationName;
                """;

            await using var command =
                new SqlCommand(
                    sql,
                    connection);

            command.Parameters.Add(
                "@PhoneDigits",
                SqlDbType.NVarChar,
                10)
                .Value = phoneDigits;

            await using var reader =
                await command.ExecuteReaderAsync();

            var matchingUsers =
                new Dictionary<int, InternalLoginLookupResult>();

            while (await reader.ReadAsync())
            {
                var userID =
                    Convert.ToInt32(
                        reader["UserID"]);

                if (!matchingUsers.TryGetValue(
                        userID,
                        out var user))
                {
                    user =
                        new InternalLoginLookupResult
                        {
                            UserID =
                                userID,

                            UserName =
                                reader["UserName"]?.ToString()
                                ?? string.Empty,

                            EmailAddress =
                                reader["EmailAddress"]?.ToString()
                                ?? string.Empty,

                            PhoneNumber =
                                reader["PhoneNumber"]?.ToString()
                                ?? string.Empty
                        };

                    matchingUsers[userID] =
                        user;
                }

                var EMAOrganizationID =
                    Convert.ToInt32(
                        reader["EMAOrganizationID"]);

                if (user.Organizations.All(
                        organization =>
                            organization.EMAOrganizationID !=
                            EMAOrganizationID))
                {
                    user.Organizations.Add(
                        new LoginOrganizationChoiceDto
                        {
                            EMAOrganizationID =
                                EMAOrganizationID,

                            OrganizationName =
                                reader["OrganizationName"]?.ToString()
                                ?? $"Organization {EMAOrganizationID}"
                        });
                }
            }

            if (matchingUsers.Count == 0)
            {
                return new NotFoundObjectResult(new
                {
                    message =
                        "That phone number is not found in our database. Please check the number or register your organization."
                });
            }

            if (matchingUsers.Count > 1)
            {
                return new ObjectResult(new
                {
                    message =
                        "More than one active user has that phone number. Phone numbers must be unique before login verification can continue."
                })
                {
                    StatusCode =
                        StatusCodes.Status409Conflict
                };
            }

            var internalResult =
                matchingUsers
                    .Values
                    .Single();

            if (string.IsNullOrWhiteSpace(
                    internalResult.EmailAddress))
            {
                return new ObjectResult(new
                {
                    message =
                        "This user does not have an email address on file for login verification."
                })
                {
                    StatusCode =
                        StatusCodes.Status409Conflict
                };
            }

            var verificationRecord =
                await _twoFactorCodeStore.CreateCodeAsync(
                    TwoFactorPurpose.OrganizationLogin,
                    internalResult.UserID,
                    internalResult.EmailAddress,
                    request.HttpContext.RequestAborted);

            try
            {
                await _twoFactorEmailSender.SendCodeAsync(
                    internalResult.EmailAddress,
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
                "Organization login verification started for UserID {UserID}.",
                internalResult.UserID);

            var response =
                new LoginLookupResponseDto
                {
                    UserID =
                        internalResult.UserID,

                    UserName =
                        internalResult.UserName,

                    EmailAddress =
                        string.Empty,

                    PhoneNumber =
                        internalResult.PhoneNumber,

                    VerificationID =
                        verificationRecord.VerificationID,

                    VerificationMessage =
                        VerificationMessage,

                    Organizations =
                        internalResult
                            .Organizations
                            .OrderBy(organization =>
                                organization.OrganizationName)
                            .ToList()
                };

            return new OkObjectResult(
                response);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error looking up login user.");

            return new ObjectResult(new
            {
                message =
                    "Unable to look up the user."
            })
            {
                StatusCode =
                    StatusCodes.Status500InternalServerError
            };
        }
    }

    private static string DigitsOnly(
        string value)
    {
        return new string(
            value
                .Where(char.IsDigit)
                .ToArray());
    }

    private static bool IsPasswordlessLoginEnabled()
    {
        return ApiRuntimeEnvironment
            .IsDevelopment;
    }

    private sealed class InternalLoginLookupResult
    {
        public int UserID { get; init; }

        public string UserName { get; init; } = string.Empty;

        public string EmailAddress { get; init; } = string.Empty;

        public string PhoneNumber { get; init; } = string.Empty;

        public List<LoginOrganizationChoiceDto> Organizations { get; init; } = new();
    }
}
