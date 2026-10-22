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

public sealed class LookupDonorLogin
{
    private const string VerificationMessage =
        "We have sent a 6-digit code to the email address on file.";

    private readonly ILogger<LookupDonorLogin> _logger;
    private readonly string _connectionString;
    private readonly ITwoFactorCodeStore _twoFactorCodeStore;
    private readonly ITwoFactorEmailSender _twoFactorEmailSender;

    public LookupDonorLogin(
        ILogger<LookupDonorLogin> logger,
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

    [Function("LookupDonorLogin")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,             "post",
            Route = "donor-login/lookup")]
        HttpRequest request)
    {
        DonorLoginLookupRequestDto? lookupRequest;

        try
        {
            lookupRequest =
                await JsonSerializer.DeserializeAsync<DonorLoginLookupRequestDto>(
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
                    "Invalid donor login lookup request."
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
            new string(
                identifier
                    .Where(char.IsDigit)
                    .ToArray());

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
                SELECT
                    d.EMADonorID,
                    d.EMAOrganizationID,
                    o.OrganizationName,
                    d.OfficialDonorName,
                    d.InteracDonorName,
                    d.EmailAddress,
                    d.PhoneNumber
                FROM dbo.Donor d
                INNER JOIN dbo.Organization o
                    ON d.EMAOrganizationID =
                       o.EMAOrganizationID
                WHERE RIGHT(
                    REPLACE(
                        REPLACE(
                            REPLACE(
                                REPLACE(
                                    REPLACE(
                                        REPLACE(
                                            ISNULL(d.PhoneNumber, ''),
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
                    o.OrganizationName,
                    d.EMADonorID;
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

            var matchingDonors =
                new Dictionary<int, InternalDonorLookupResult>();

            while (await reader.ReadAsync())
            {
                var donorID =
                    Convert.ToInt32(
                        reader["EMADonorID"]);

                if (!matchingDonors.TryGetValue(
                        donorID,
                        out var donor))
                {
                    donor =
                        new InternalDonorLookupResult
                        {
                            DonorID =
                                donorID,

                            DonorName =
                                GetDonorDisplayName(
                                    reader),

                            EmailAddress =
                                reader["EmailAddress"]?.ToString()
                                ?? string.Empty,

                            PhoneNumber =
                                reader["PhoneNumber"]?.ToString()
                                ?? string.Empty
                        };

                    matchingDonors[donorID] =
                        donor;
                }

                var EMAOrganizationID =
                    Convert.ToInt32(
                        reader["EMAOrganizationID"]);

                if (donor.Organizations.All(
                        organization =>
                            organization.EMAOrganizationID !=
                            EMAOrganizationID))
                {
                    donor.Organizations.Add(
                        new DonorLoginOrganizationChoiceDto
                        {
                            DonorID =
                                donorID,

                            EMAOrganizationID =
                                EMAOrganizationID,

                            OrganizationName =
                                reader["OrganizationName"]?.ToString()
                                ?? $"Organization {EMAOrganizationID}"
                        });
                }
            }

            if (matchingDonors.Count == 0)
            {
                return new NotFoundObjectResult(new
                {
                    message =
                        "That phone number is not found in our database. Please check the number or register as a donor."
                });
            }

            if (matchingDonors.Count > 1)
            {
                return new ObjectResult(new
                {
                    message =
                        "More than one donor has that phone number. Phone numbers must be unique before donor login verification can continue."
                })
                {
                    StatusCode =
                        StatusCodes.Status409Conflict
                };
            }

            var internalResult =
                matchingDonors
                    .Values
                    .Single();

            if (string.IsNullOrWhiteSpace(
                    internalResult.EmailAddress))
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
                    internalResult.DonorID,
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
                "Donor login verification started for DonorID {DonorID}.",
                internalResult.DonorID);

            var response =
                new DonorLoginLookupResponseDto
                {
                    DonorName =
                        internalResult.DonorName,

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
                "Error looking up donor login.");

            return new ObjectResult(new
            {
                message =
                    "Unable to look up donor login."
            })
            {
                StatusCode =
                    StatusCodes.Status500InternalServerError
            };
        }
    }

    private static string GetDonorDisplayName(
        SqlDataReader reader)
    {
        var officialName =
            reader["OfficialDonorName"]?.ToString();

        if (!string.IsNullOrWhiteSpace(
                officialName))
        {
            return officialName;
        }

        var interacName =
            reader["InteracDonorName"]?.ToString();

        if (!string.IsNullOrWhiteSpace(
                interacName))
        {
            return interacName;
        }

        return "Donor";
    }

    private sealed class InternalDonorLookupResult
    {
        public int DonorID { get; init; }

        public string DonorName { get; init; } = string.Empty;

        public string EmailAddress { get; init; } = string.Empty;

        public string PhoneNumber { get; init; } = string.Empty;

        public List<DonorLoginOrganizationChoiceDto> Organizations { get; init; } = new();
    }
}
