using System.Data;
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

public sealed class VerifyDonorSelfRegistration
{
    private const int BrochureCategoryID =
        10;

    private const int DonorsCategoryID =
        30;

    private readonly ILogger<VerifyDonorSelfRegistration>
        _logger;

    private readonly string _connectionString;

    private readonly IPendingDonorRegistrationStore
        _pendingRegistrationStore;

    private readonly IApiSessionService
        _apiSessionService;

    private readonly int
        _createdByUserID;

    private readonly int
        _platformOrganizationID;

    public VerifyDonorSelfRegistration(
        ILogger<VerifyDonorSelfRegistration> logger,
        IPendingDonorRegistrationStore pendingRegistrationStore,
        IApiSessionService apiSessionService,
        ApiRequestBoundaryValidator authorization)
    {
        _logger =
            logger;

        _pendingRegistrationStore =
            pendingRegistrationStore;

        _apiSessionService =
            apiSessionService;

        _platformOrganizationID =
            authorization
                .PlatformOrganizationID;

        _createdByUserID =
            RequiredSecurityConfiguration
                .GetPositiveInt(
                    "AUTOMATED_SERVICE_USER_ID");

        _connectionString =
            Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString.");
    }

    [Function("VerifyDonorSelfRegistration")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "post",
            Route = "donor-registration/verify")]
        HttpRequest request)
    {
        VerifyDonorSelfRegistrationRequestDto?
            verifyRequest;

        try
        {
            verifyRequest =
                await JsonSerializer
                    .DeserializeAsync<
                        VerifyDonorSelfRegistrationRequestDto>(
                        request.Body,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive =
                                true
                        });
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult(
                new
                {
                    message =
                        "Invalid donor registration verification request."
                });
        }

        if (verifyRequest is null ||
            string.IsNullOrWhiteSpace(
                verifyRequest.RegistrationID) ||
            string.IsNullOrWhiteSpace(
                verifyRequest.Code))
        {
            return new BadRequestObjectResult(
                new
                {
                    message =
                        "Registration ID and code are required."
                });
        }

        var registration =
            await _pendingRegistrationStore
                .TryVerifyAndConsumeAsync(
                    verifyRequest.RegistrationID,
                    verifyRequest.Code,
                    request.HttpContext
                        .RequestAborted);

        if (registration is null)
        {
            return new UnauthorizedObjectResult(
                new
                {
                    message =
                        "The verification code is invalid or expired."
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

            await using var transaction =
                (SqlTransaction)await connection
                    .BeginTransactionAsync(
                        IsolationLevel.Serializable,
                        request.HttpContext
                            .RequestAborted);

            if (!await OrganizationAllowsDonorAccessAsync(
                    connection,
                    transaction,
                    registration.EMAOrganizationID,
                    _platformOrganizationID,
                    request.HttpContext
                        .RequestAborted))
            {
                await transaction.RollbackAsync(
                    request.HttpContext
                        .RequestAborted);

                return new ObjectResult(
                    new
                    {
                        message =
                            "Donor registration is not available for the selected organization."
                    })
                {
                    StatusCode =
                        StatusCodes.Status409Conflict
                };
            }

            if (await DonorPhoneExistsAsync(
                    connection,
                    transaction,
                    registration.EMAOrganizationID,
                    DigitsOnly(
                        registration.PhoneNumber),
                    request.HttpContext
                        .RequestAborted))
            {
                await transaction.RollbackAsync(
                    request.HttpContext
                        .RequestAborted);

                return new ObjectResult(
                    new
                    {
                        message =
                            "A donor record already exists with that phone number for this organization. Please use donor login."
                    })
                {
                    StatusCode =
                        StatusCodes.Status409Conflict
                };
            }

            var donorID =
                await CreateDonorAsync(
                    connection,
                    transaction,
                    registration,
                    request.HttpContext
                        .RequestAborted);

            await transaction.CommitAsync(
                request.HttpContext
                    .RequestAborted);

            var loginResponse =
                await BuildDonorLoginResponseAsync(
                    connection,
                    registration.EMAOrganizationID,
                    donorID);

            if (loginResponse is null)
            {
                return new ObjectResult(
                    new
                    {
                        message =
                            "Donor record was created, but login could not be completed because this organization does not have an active donor role."
                    })
                {
                    StatusCode =
                        StatusCodes.Status409Conflict
                };
            }

            var accessToken =
                await _apiSessionService
                    .CreateSessionAsync(
                        loginResponse,
                        request.HttpContext
                            .RequestAborted);

            _logger.LogInformation(
                "Donor self-registration verified and donor {DonorID} created for organization {EMAOrganizationID}.",
                donorID,
                registration.EMAOrganizationID);

            return new OkObjectResult(
                CreateAuthenticatedResponse(
                    loginResponse,
                    accessToken));
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error verifying donor self-registration.");

            return new ObjectResult(
                new
                {
                    message =
                        "Unable to complete donor registration."
                })
            {
                StatusCode =
                    StatusCodes
                        .Status500InternalServerError
            };
        }
    }

    private static object CreateAuthenticatedResponse(
        LoginResponseDto loginResponse,
        ApiAccessToken accessToken)
    {
        return new
        {
            loginResponse.UserID,
            loginResponse.UserName,
            loginResponse.EmailAddress,
            loginResponse.PhoneNumber,
            loginResponse.EMAOrganizationID,
            loginResponse.OrganizationName,
            loginResponse.Roles,
            loginResponse.AllowedCategoryIDs,
            loginResponse.DefaultCategoryID,
            loginResponse.IsDonorSession,
            loginResponse.DonorID,

            AccessToken =
                accessToken.Token,

            AccessTokenExpiresAtUtc =
                accessToken.ExpiresAtUtc
        };
    }

    private async Task<int> CreateDonorAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        PendingDonorRegistrationRecord registration,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO dbo.Donor
            (
                EMAOrganizationID,
                EmailAddress,
                InteracDonorName,
                OfficialDonorName,
                Address,
                City,
                ProvinceStateCode,
                PostalCode,
                Country,
                PhoneNumber,
                DateTimeCreated,
                CreatedByUserID
            )
            OUTPUT INSERTED.EMADonorID
            VALUES
            (
                @EMAOrganizationID,
                @EmailAddress,
                @InteracDonorName,
                @OfficialDonorName,
                @Address,
                @City,
                @ProvinceStateCode,
                @PostalCode,
                @Country,
                @PhoneNumber,
                SYSUTCDATETIME(),
                @CreatedByUserID
            );
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection,
                transaction);

        command.Parameters.Add(
            "@EMAOrganizationID",
            SqlDbType.Int)
            .Value =
                registration.EMAOrganizationID;

        command.Parameters.Add(
            "@EmailAddress",
            SqlDbType.NVarChar,
            320)
            .Value =
                registration.EmailAddress;

        command.Parameters.Add(
            "@InteracDonorName",
            SqlDbType.NVarChar,
            200)
            .Value =
                string.IsNullOrWhiteSpace(
                    registration.InteracDonorName)
                    ? DBNull.Value
                    : registration.InteracDonorName;

        command.Parameters.Add(
            "@OfficialDonorName",
            SqlDbType.NVarChar,
            200)
            .Value =
                registration.OfficialDonorName;

        command.Parameters.Add(
            "@Address",
            SqlDbType.NVarChar,
            250)
            .Value =
                registration.Address;

        command.Parameters.Add(
            "@City",
            SqlDbType.NVarChar,
            100)
            .Value =
                registration.City;

        command.Parameters.Add(
            "@ProvinceStateCode",
            SqlDbType.NVarChar,
            2)
            .Value =
                registration
                    .ProvinceStateCode
                    .Trim()
                    .ToUpperInvariant();

        command.Parameters.Add(
            "@PostalCode",
            SqlDbType.NVarChar,
            50)
            .Value =
                registration.PostalCode;

        command.Parameters.Add(
            "@Country",
            SqlDbType.NVarChar,
            100)
            .Value =
                registration.Country;

        command.Parameters.Add(
            "@PhoneNumber",
            SqlDbType.NVarChar,
            50)
            .Value =
                registration.PhoneNumber;

        command.Parameters.Add(
            "@CreatedByUserID",
            SqlDbType.Int)
            .Value =
                _createdByUserID;

        var donorID =
            await command.ExecuteScalarAsync(
                cancellationToken);

        return Convert.ToInt32(
            donorID);
    }

    private static async Task<bool>
        DonorPhoneExistsAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            int EMAOrganizationID,
            string phoneDigits,
            CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM dbo.Donor WITH
                (UPDLOCK, HOLDLOCK)
            WHERE EMAOrganizationID = @EMAOrganizationID
              AND RIGHT(
                  REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(PhoneNumber, ''), '(', ''), ')', ''), '-', ''), ' ', ''), '.', ''), '+', ''),
                  10
              ) = @PhoneDigits;
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection,
                transaction);

        command.Parameters.Add(
            "@EMAOrganizationID",
            SqlDbType.Int)
            .Value = EMAOrganizationID;

        command.Parameters.Add(
            "@PhoneDigits",
            SqlDbType.NVarChar,
            10)
            .Value = phoneDigits;

        var count =
            Convert.ToInt32(
                await command
                    .ExecuteScalarAsync(
                        cancellationToken));

        return count > 0;
    }

    private static async Task<bool>
        OrganizationAllowsDonorAccessAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            int EMAOrganizationID,
            int platformOrganizationID,
            CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM dbo.Organization organization
                WITH (UPDLOCK, HOLDLOCK)
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
            new SqlCommand(
                sql,
                connection,
                transaction);

        command.Parameters.Add(
            "@EMAOrganizationID",
            SqlDbType.Int)
            .Value =
                EMAOrganizationID;

        command.Parameters.Add(
            "@PlatformOrganizationID",
            SqlDbType.Int)
            .Value =
                platformOrganizationID;

        return Convert.ToInt32(
                   await command.ExecuteScalarAsync(
                       cancellationToken)) >
               0;
    }

    private static async Task<LoginResponseDto?>
        BuildDonorLoginResponseAsync(
            SqlConnection connection,
            int EMAOrganizationID,
            int donorID)
    {
        const string sql = """
            SELECT
                d.EMADonorID,
                d.EMAOrganizationID,
                d.OfficialDonorName,
                d.InteracDonorName,
                d.EmailAddress,
                d.PhoneNumber,
                o.OrganizationName,
                o.DonorRoleID,
                r.RoleName
            FROM dbo.Donor d
            INNER JOIN dbo.Organization o
                ON d.EMAOrganizationID =
                   o.EMAOrganizationID
            INNER JOIN dbo.[Role] r
                ON o.DonorRoleID =
                   r.RoleID
            WHERE d.EMADonorID = @DonorID
              AND d.EMAOrganizationID = @EMAOrganizationID
              AND ISNULL(
                      o.OrganizationStatusID,
                      -1) <> 0
              AND o.DonorRoleID IS NOT NULL
              AND ISNULL(r.IsActive, 'Y') = 'Y';
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters.Add(
            "@DonorID",
            SqlDbType.Int)
            .Value = donorID;

        command.Parameters.Add(
            "@EMAOrganizationID",
            SqlDbType.Int)
            .Value = EMAOrganizationID;

        await using var reader =
            await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        var donorRoleID =
            Convert.ToInt32(
                reader["DonorRoleID"]);

        return new LoginResponseDto
        {
            UserID =
                donorID,

            UserName =
                GetDonorDisplayName(
                    reader),

            EmailAddress =
                reader["EmailAddress"]?.ToString()
                ?? string.Empty,

            PhoneNumber =
                reader["PhoneNumber"]?.ToString()
                ?? string.Empty,

            EMAOrganizationID =
                EMAOrganizationID,

            OrganizationName =
                reader["OrganizationName"]?.ToString()
                ?? string.Empty,

            Roles =
                new List<LoginRoleDto>
                {
                    new()
                    {
                        RoleID =
                            donorRoleID,

                        RoleName =
                            reader["RoleName"]?.ToString()
                            ?? string.Empty
                    }
                },

            AllowedCategoryIDs =
                new List<int>
                {
                    BrochureCategoryID,
                    DonorsCategoryID
                },

            DefaultCategoryID =
                DonorsCategoryID,

            IsDonorSession =
                true,

            DonorID =
                donorID
        };
    }

    private static string GetDonorDisplayName(
        SqlDataReader reader)
    {
        var officialName =
            reader["OfficialDonorName"]
                ?.ToString();

        if (!string.IsNullOrWhiteSpace(
                officialName))
        {
            return officialName;
        }

        var interacName =
            reader["InteracDonorName"]
                ?.ToString();

        if (!string.IsNullOrWhiteSpace(
                interacName))
        {
            return interacName;
        }

        return "Donor";
    }

    private static string DigitsOnly(
        string value)
    {
        return new string(
            value
                .Where(char.IsDigit)
                .ToArray());
    }
}