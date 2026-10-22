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

public sealed class VerifyDonorLoginCode
{
    private const int BrochureCategoryID =
        10;

    private const int DonorsCategoryID =
        30;

    private readonly ILogger<VerifyDonorLoginCode>
        _logger;

    private readonly string _connectionString;

    private readonly ITwoFactorCodeStore
        _twoFactorCodeStore;

    private readonly IApiSessionService
        _apiSessionService;

    private readonly int
        _platformOrganizationID;

    public VerifyDonorLoginCode(
        ILogger<VerifyDonorLoginCode> logger,
        ITwoFactorCodeStore twoFactorCodeStore,
        IApiSessionService apiSessionService,
        ApiRequestBoundaryValidator authorization)
    {
        _logger =
            logger;

        _twoFactorCodeStore =
            twoFactorCodeStore;

        _apiSessionService =
            apiSessionService;

        _platformOrganizationID =
            authorization
                .PlatformOrganizationID;

        _connectionString =
            Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString.");
    }

    [Function("VerifyDonorLoginCode")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,             "post",
            Route = "donor-login/verify-code")]
        HttpRequest request)
    {
        VerifyDonorLoginCodeRequestDto? verifyRequest;

        try
        {
            verifyRequest =
                await JsonSerializer
                    .DeserializeAsync<
                        VerifyDonorLoginCodeRequestDto>(
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
                        "Invalid donor verification request."
                });
        }

        if (verifyRequest is null ||
            verifyRequest.EMAOrganizationID <= 0 ||
            verifyRequest.DonorID <= 0 ||
            string.IsNullOrWhiteSpace(
                verifyRequest.VerificationID) ||
            string.IsNullOrWhiteSpace(
                verifyRequest.Code))
        {
            return new BadRequestObjectResult(
                new
                {
                    message =
                        "Verification ID, code, organization, and donor are required."
                });
        }

        var isVerified =
            await _twoFactorCodeStore
                .VerifyCodeAsync(
                    verifyRequest.VerificationID,
                    TwoFactorPurpose.DonorLogin,
                    verifyRequest.DonorID,
                    emailAddress: null,
                    verifyRequest.Code,
                    consumeOnSuccess: true,
                    cancellationToken:
                        request.HttpContext
                            .RequestAborted);

        if (!isVerified)
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

            var loginResponse =
                await BuildDonorLoginResponseAsync(
                    connection,
                    verifyRequest.EMAOrganizationID,
                    verifyRequest.DonorID,
                    _platformOrganizationID);

            if (loginResponse is null)
            {
                return new UnauthorizedObjectResult(
                    new
                    {
                        message =
                            "Donor login could not be completed for the selected organization."
                    });
            }

            var accessToken =
                await _apiSessionService
                    .CreateSessionAsync(
                        loginResponse,
                        request.HttpContext
                            .RequestAborted);

            _logger.LogInformation(
                "Donor login code verified for DonorID {DonorID}, EMAOrganizationID {EMAOrganizationID}.",
                verifyRequest.DonorID,
                verifyRequest.EMAOrganizationID);

            return new OkObjectResult(
                CreateAuthenticatedResponse(
                    loginResponse,
                    accessToken));
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error verifying donor login code.");

            return new ObjectResult(
                new
                {
                    message =
                        "Unable to verify donor login code."
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

    internal static async Task<LoginResponseDto?>
        BuildDonorLoginResponseAsync(
            SqlConnection connection,
            int EMAOrganizationID,
            int donorID,
            int platformOrganizationID)
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
              AND o.EMAOrganizationID <>
                  @PlatformOrganizationID
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

        command.Parameters.Add(
            "@PlatformOrganizationID",
            SqlDbType.Int)
            .Value =
                platformOrganizationID;

        await using var reader =
            await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        var donorRoleID =
            Convert.ToInt32(
                reader["DonorRoleID"]);

        var donorName =
            GetDonorDisplayName(
                reader);

        return new LoginResponseDto
        {
            UserID =
                donorID,

            UserName =
                donorName,

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
}
