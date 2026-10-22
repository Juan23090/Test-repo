using System.Data;
using System.Net.Mail;
using System.Text.Json;
using EMA.Api.DTOs;
using EMA.Api.Services.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Authentication;

public sealed class StartDonorSelfRegistrationVerification
{
    private const string VerificationMessage =
        "We have sent a 6-digit code to the email address entered. Please check that the phone number is correct before entering the code.";

    private readonly ILogger<StartDonorSelfRegistrationVerification> _logger;
    private readonly string _connectionString;
    private readonly IPendingDonorRegistrationStore _pendingRegistrationStore;
    private readonly ITwoFactorEmailSender _twoFactorEmailSender;
    private readonly int _platformOrganizationID;

    public StartDonorSelfRegistrationVerification(
        ILogger<StartDonorSelfRegistrationVerification> logger,
        IPendingDonorRegistrationStore pendingRegistrationStore,
        ITwoFactorEmailSender twoFactorEmailSender,
        ApiRequestBoundaryValidator authorization)
    {
        _logger = logger;
        _pendingRegistrationStore = pendingRegistrationStore;
        _twoFactorEmailSender = twoFactorEmailSender;
        _platformOrganizationID =
            authorization
                .PlatformOrganizationID;

        _connectionString =
            Environment.GetEnvironmentVariable("SqlConnectionString")
            ?? throw new InvalidOperationException("Missing SqlConnectionString.");
    }

    [Function("StartDonorSelfRegistrationVerification")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,             "post",
            Route = "donor-registration/start-verification")]
        HttpRequest request)
    {
        StartDonorSelfRegistrationVerificationRequestDto? registrationRequest;

        try
        {
            registrationRequest =
                await JsonSerializer.DeserializeAsync<StartDonorSelfRegistrationVerificationRequestDto>(
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
                message = "Invalid donor registration request."
            });
        }

        var validationMessage =
            ValidateRequest(registrationRequest);

        if (!string.IsNullOrWhiteSpace(validationMessage))
        {
            return new BadRequestObjectResult(new
            {
                message = validationMessage
            });
        }

        var normalizedPhone =
            FormatPhoneNumber(DigitsOnly(registrationRequest!.PhoneNumber));

        var normalizedEmail =
            registrationRequest.EmailAddress.Trim();

        var normalizedProvinceStateCode =
            ProvinceStateCodeNormalizer.Normalize(
                registrationRequest.ProvinceStateCode);

        if (normalizedProvinceStateCode is null)
        {
            return new BadRequestObjectResult(new
            {
                message =
                    "Please enter a valid Canadian province or U.S. state using its name or 2-letter code."
            });
        }

        try
        {
            await using var connection =
                new SqlConnection(_connectionString);

            await connection.OpenAsync();

            if (!await OrganizationAllowsDonorAccessAsync(
                    connection,
                    registrationRequest.EMAOrganizationID,
                    _platformOrganizationID))
            {
                return new BadRequestObjectResult(new
                {
                    message = "Please select a valid organization."
                });
            }

            if (await DonorPhoneExistsAsync(
                    connection,
                    registrationRequest.EMAOrganizationID,
                    DigitsOnly(registrationRequest.PhoneNumber)))
            {
                return new ObjectResult(new
                {
                    message = "A donor record already exists with that phone number for this organization. Please use donor login."
                })
                {
                    StatusCode = StatusCodes.Status409Conflict
                };
            }

            var pendingRegistration =
                await _pendingRegistrationStore
                    .CreateRegistrationAsync(
                    registrationRequest.EMAOrganizationID,
                    registrationRequest.OfficialDonorName.Trim(),
                    NormalizeOptional(registrationRequest.InteracDonorName),
                    registrationRequest.Address.Trim(),
                    registrationRequest.City.Trim(),
                    normalizedProvinceStateCode,
                    registrationRequest.PostalCode.Trim(),
                    registrationRequest.Country.Trim(),
                    normalizedPhone,
                    normalizedEmail,
                    request.HttpContext
                        .RequestAborted);

            try
            {
                await _twoFactorEmailSender
                    .SendDonorRegistrationCodeAsync(
                        normalizedEmail,
                        pendingRegistration.Code,
                        normalizedPhone,
                        request.HttpContext
                            .RequestAborted);
            }
            catch
            {
                await _pendingRegistrationStore
                    .RevokeAsync(
                        pendingRegistration
                            .RegistrationID,
                        request.HttpContext
                            .RequestAborted);

                throw;
            }

            _logger.LogInformation(
                "Donor self-registration verification started for EMAOrganizationID {EMAOrganizationID}.",
                registrationRequest.EMAOrganizationID);

            return new OkObjectResult(
                new StartDonorSelfRegistrationVerificationResponseDto
                {
                    RegistrationID = pendingRegistration.RegistrationID,
                    Message = VerificationMessage
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
                "Error starting donor self-registration verification.");

            return new ObjectResult(new
            {
                message = "Unable to start donor registration verification."
            })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }

    private static string? ValidateRequest(
        StartDonorSelfRegistrationVerificationRequestDto? registrationRequest)
    {
        if (registrationRequest is null)
        {
            return "Donor registration information is required.";
        }

        if (registrationRequest.EMAOrganizationID <= 0)
        {
            return "Organization is required.";
        }

        if (string.IsNullOrWhiteSpace(registrationRequest.OfficialDonorName))
        {
            return "Official Donor Name is required.";
        }

        if (registrationRequest.OfficialDonorName.Trim().Length > 200 ||
            (
                registrationRequest
                    .InteracDonorName
                    ?.Trim()
                    .Length
                ?? 0
            ) > 200)
        {
            return "Donor names cannot exceed 200 characters.";
        }

        if (string.IsNullOrWhiteSpace(registrationRequest.Address))
        {
            return "Address is required.";
        }

        if (registrationRequest.Address.Trim().Length > 250)
        {
            return "Address cannot exceed 250 characters.";
        }

        if (string.IsNullOrWhiteSpace(registrationRequest.City))
        {
            return "City/Town is required.";
        }

        if (registrationRequest.City.Trim().Length > 100)
        {
            return "City/Town cannot exceed 100 characters.";
        }

        if (string.IsNullOrWhiteSpace(registrationRequest.ProvinceStateCode))
        {
            return "Prov/State is required.";
        }

        if (registrationRequest.ProvinceStateCode.Trim().Length > 2)
        {
            return "Prov/State cannot exceed 2 characters.";
        }

        if (string.IsNullOrWhiteSpace(registrationRequest.PostalCode))
        {
            return "Postal/Zip is required.";
        }

        if (registrationRequest.PostalCode.Trim().Length > 50)
        {
            return "Postal/Zip cannot exceed 50 characters.";
        }

        if (string.IsNullOrWhiteSpace(registrationRequest.Country))
        {
            return "Country is required.";
        }

        if (registrationRequest.Country.Trim().Length > 100)
        {
            return "Country cannot exceed 100 characters.";
        }

        if (DigitsOnly(registrationRequest.PhoneNumber).Length != 10)
        {
            return "Please input a valid 10 digit phone number.";
        }

        var emailAddress =
            registrationRequest.EmailAddress
                ?.Trim();

        if (string.IsNullOrWhiteSpace(
                emailAddress) ||
            emailAddress.Length > 320 ||
            !MailAddress.TryCreate(
                emailAddress,
                out var parsedAddress) ||
            !string.Equals(
                parsedAddress.Address,
                emailAddress,
                StringComparison.OrdinalIgnoreCase))
        {
            return "Please input a valid email address.";
        }

        return null;
    }

    private static async Task<bool> OrganizationAllowsDonorAccessAsync(
        SqlConnection connection,
        int EMAOrganizationID,
        int platformOrganizationID)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM dbo.Organization organization
            INNER JOIN dbo.[Role] donorRole
                ON donorRole.RoleID =
                   organization.DonorRoleID
            WHERE organization.EMAOrganizationID =
                  @EMAOrganizationID
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
            new SqlCommand(sql, connection);

        command.Parameters.Add("@EMAOrganizationID", SqlDbType.Int).Value =
            EMAOrganizationID;

        command.Parameters.Add(
            "@PlatformOrganizationID",
            SqlDbType.Int)
            .Value =
                platformOrganizationID;

        var count =
            Convert.ToInt32(await command.ExecuteScalarAsync());

        return count > 0;
    }

    private static async Task<bool> DonorPhoneExistsAsync(
        SqlConnection connection,
        int EMAOrganizationID,
        string phoneDigits)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM dbo.Donor
            WHERE EMAOrganizationID = @EMAOrganizationID
              AND RIGHT(
                  REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(PhoneNumber, ''), '(', ''), ')', ''), '-', ''), ' ', ''), '.', ''), '+', ''),
                  10
              ) = @PhoneDigits;
            """;

        await using var command =
            new SqlCommand(sql, connection);

        command.Parameters.Add("@EMAOrganizationID", SqlDbType.Int).Value =
            EMAOrganizationID;

        command.Parameters.Add("@PhoneDigits", SqlDbType.NVarChar, 10).Value =
            phoneDigits;

        var count =
            Convert.ToInt32(await command.ExecuteScalarAsync());

        return count > 0;
    }

    private static string? NormalizeOptional(
        string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static string DigitsOnly(
        string value)
    {
        return new string(
            value
                .Where(char.IsDigit)
                .ToArray());
    }

    private static string FormatPhoneNumber(
        string phoneDigits)
    {
        if (phoneDigits.Length != 10)
        {
            return phoneDigits;
        }

        return $"{phoneDigits[..3]}-{phoneDigits.Substring(3, 3)}-{phoneDigits.Substring(6, 4)}";
    }
}
