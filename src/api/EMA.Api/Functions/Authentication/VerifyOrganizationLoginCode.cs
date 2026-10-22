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

public sealed class VerifyOrganizationLoginCode
{
    private const int BrochureCategoryID =
        10;

    private const int OperationsCategoryID =
        20;

    private const int DonorsCategoryID =
        30;

    private const int SupportCategoryID =
        90;

    private readonly ILogger<VerifyOrganizationLoginCode> _logger;
    private readonly string _connectionString;
    private readonly ITwoFactorCodeStore _twoFactorCodeStore;
    private readonly IApiSessionService _apiSessionService;

    public VerifyOrganizationLoginCode(
        ILogger<VerifyOrganizationLoginCode> logger,
        ITwoFactorCodeStore twoFactorCodeStore,
        IApiSessionService apiSessionService)
    {
        _logger =
            logger;

        _twoFactorCodeStore =
            twoFactorCodeStore;

        _apiSessionService =
            apiSessionService;

        _connectionString =
            Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString.");
    }

    [Function("VerifyOrganizationLoginCode")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,             "post",
            Route = "login/verify-code")]
        HttpRequest request)
    {
        VerifyOrganizationLoginCodeRequestDto? verifyRequest;

        try
        {
            verifyRequest =
                await JsonSerializer.DeserializeAsync<VerifyOrganizationLoginCodeRequestDto>(
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
                    "Invalid verification request."
            });
        }

        if (verifyRequest is null ||
            verifyRequest.EMAOrganizationID <= 0 ||
            verifyRequest.UserID <= 0 ||
            string.IsNullOrWhiteSpace(
                verifyRequest.VerificationID) ||
            string.IsNullOrWhiteSpace(
                verifyRequest.Code))
        {
            return new BadRequestObjectResult(new
            {
                message =
                    "Verification ID, code, organization, and user are required."
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

            var organizationUserID =
                await GetOrganizationUserIDAsync(
                    connection,
                    verifyRequest
                        .EMAOrganizationID,
                    verifyRequest.UserID,
                    request.HttpContext
                        .RequestAborted);

            if (!organizationUserID.HasValue)
            {
                return InvalidCode();
            }

            var isVerified =
                await _twoFactorCodeStore
                    .VerifyCodeAsync(
                        verifyRequest
                            .VerificationID,
                        TwoFactorPurpose
                            .OrganizationLogin,
                        organizationUserID
                            .Value,
                        emailAddress: null,
                        verifyRequest.Code,
                        consumeOnSuccess: true,
                        cancellationToken:
                            request.HttpContext
                                .RequestAborted);

            if (!isVerified)
            {
                return InvalidCode();
            }

            await MarkEmailAddressValidatedAsync(
                connection,
                verifyRequest.UserID,
                request.HttpContext
                    .RequestAborted);

            var loginResponse =
                await BuildLoginResponseAsync(
                    connection,
                    verifyRequest.EMAOrganizationID,
                    verifyRequest.UserID);

            if (loginResponse is null)
            {
                return new UnauthorizedObjectResult(new
                {
                    message =
                        "Login could not be completed for the selected organization."
                });
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

            _logger.LogInformation(
                "Organization login code verified for UserID {UserID}, EMAOrganizationID {EMAOrganizationID}.",
                verifyRequest.UserID,
                verifyRequest.EMAOrganizationID);

            return new OkObjectResult(
                loginResponse);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error verifying organization login code.");

            return new ObjectResult(new
            {
                message =
                    "Unable to verify login code."
            })
            {
                StatusCode =
                    StatusCodes.Status500InternalServerError
            };
        }
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

    private static async Task<int?>
        GetOrganizationUserIDAsync(
            SqlConnection connection,
            int organizationID,
            int userID,
            CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP (1)
                OrganizationUserID
            FROM dbo.OrganizationUser
            INNER JOIN dbo.[User] appUser
                ON appUser.UserID =
                   OrganizationUser.UserID
            WHERE OrganizationUser.EMAOrganizationID =
                  @EMAOrganizationID
              AND OrganizationUser.UserID = @UserID
              AND OrganizationUser.OrganizationUserStatusID = 10
              AND appUser.SandboxOrganizationID IS NULL;
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters.Add(
            "@EMAOrganizationID",
            SqlDbType.Int)
            .Value =
                organizationID;

        command.Parameters.Add(
            "@UserID",
            SqlDbType.Int)
            .Value =
                userID;

        var result =
            await command.ExecuteScalarAsync(
                cancellationToken);

        return result is null ||
               result == DBNull.Value
            ? null
            : Convert.ToInt32(
                result);
    }

    internal static async Task
        MarkEmailAddressValidatedAsync(
            SqlConnection connection,
            int userID,
            CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE dbo.[User]
            SET EmailAddressValidated = 'Y',
                DateTimeUpdated =
                    SYSUTCDATETIME()
            WHERE UserID = @UserID
              AND ISNULL(
                      EmailAddressValidated,
                      'N') <> 'Y';
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters.Add(
            "@UserID",
            SqlDbType.Int)
            .Value =
                userID;

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    internal static async Task<LoginResponseDto?> BuildLoginResponseAsync(
        SqlConnection connection,
        int EMAOrganizationID,
        int userID)
    {
        const string userSql = """
            SELECT
                u.UserID,
                u.UserName,
                u.EmailAddress,
                u.PhoneNumber,
                o.EMAOrganizationID,
                o.OrganizationName
            FROM dbo.[User] u
            INNER JOIN dbo.OrganizationUser ou
                ON u.UserID =
                   ou.UserID
            INNER JOIN dbo.Organization o
                ON ou.EMAOrganizationID =
                   o.EMAOrganizationID
            WHERE u.UserID = @UserID
              AND o.EMAOrganizationID = @EMAOrganizationID
              AND ou.OrganizationUserStatusID = 10
              AND u.IsActive = 1
              AND u.SandboxOrganizationID IS NULL
              AND ISNULL(
                      o.OrganizationStatusID,
                      -1) <> 0;
            """;

        await using var userCommand =
            new SqlCommand(
                userSql,
                connection);

        userCommand.Parameters.Add(
            "@UserID",
            SqlDbType.Int)
            .Value = userID;

        userCommand.Parameters.Add(
            "@EMAOrganizationID",
            SqlDbType.Int)
            .Value = EMAOrganizationID;

        await using var reader =
            await userCommand.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        var response =
            new LoginResponseDto
            {
                UserID =
                    Convert.ToInt32(
                        reader["UserID"]),

                UserName =
                    reader["UserName"]?.ToString()
                    ?? string.Empty,

                EmailAddress =
                    reader["EmailAddress"]?.ToString()
                    ?? string.Empty,

                PhoneNumber =
                    reader["PhoneNumber"]?.ToString()
                    ?? string.Empty,

                EMAOrganizationID =
                    Convert.ToInt32(
                        reader["EMAOrganizationID"]),

                OrganizationName =
                    reader["OrganizationName"]?.ToString()
                    ?? string.Empty,

                DefaultCategoryID =
                    OperationsCategoryID,

                IsDonorSession =
                    false,

                DonorID =
                    null
            };

        await reader.CloseAsync();

        response.Roles =
            await GetUserRolesAsync(
                connection,
                EMAOrganizationID,
                userID);

        if (response.Roles.Count == 0)
        {
            return null;
        }

        response.AllowedCategoryIDs =
            await GetAllowedCategoryIDsAsync(
                connection,
                EMAOrganizationID,
                userID);

        if (!response
                .AllowedCategoryIDs
                .Contains(
                    BrochureCategoryID))
        {
            response
                .AllowedCategoryIDs
                .Add(
                    BrochureCategoryID);
        }

        if (response.AllowedCategoryIDs.Count == 0)
        {
            response
                .AllowedCategoryIDs
                .Add(
                    BrochureCategoryID);
        }

        if (!response
                .AllowedCategoryIDs
                .Contains(
                    response.DefaultCategoryID))
        {
            response.DefaultCategoryID =
                response.AllowedCategoryIDs.Contains(
                    OperationsCategoryID)
                    ? OperationsCategoryID
                    : response.AllowedCategoryIDs.First();
        }

        response.AllowedCategoryIDs =
            response
                .AllowedCategoryIDs
                .Distinct()
                .OrderBy(categoryID =>
                    categoryID)
                .ToList();

        return response;
    }

    private static async Task<List<LoginRoleDto>> GetUserRolesAsync(
        SqlConnection connection,
        int EMAOrganizationID,
        int userID)
    {
        const string sql = """
            SELECT DISTINCT
                r.RoleID,
                r.RoleName
            FROM dbo.OrganizationUserRole ur
            INNER JOIN dbo.[Role] r
                ON ur.RoleID =
                   r.RoleID
            WHERE ur.EMAOrganizationID =
                  @EMAOrganizationID
              AND ur.UserID = @UserID
              AND ISNULL(r.IsActive, 'Y') = 'Y'
            ORDER BY
                r.RoleName;
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters.Add(
            "@UserID",
            SqlDbType.Int)
            .Value = userID;

        command.Parameters.Add(
            "@EMAOrganizationID",
            SqlDbType.Int)
            .Value = EMAOrganizationID;

        await using var reader =
            await command.ExecuteReaderAsync();

        var roles =
            new List<LoginRoleDto>();

        while (await reader.ReadAsync())
        {
            roles.Add(
                new LoginRoleDto
                {
                    RoleID =
                        Convert.ToInt32(
                            reader["RoleID"]),

                    RoleName =
                        reader["RoleName"]?.ToString()
                        ?? string.Empty
                });
        }

        return roles;
    }

    private static async Task<List<int>> GetAllowedCategoryIDsAsync(
        SqlConnection connection,
        int EMAOrganizationID,
        int userID)
    {
        const string sql = """
            SELECT DISTINCT
                mi.MenuItemCategoryID
            FROM dbo.OrganizationUserRole ur
            INNER JOIN dbo.RoleMenuItem rmi
                ON ur.RoleID =
                   rmi.RoleID
            INNER JOIN dbo.MenuItem mi
                ON rmi.MenuItemID =
                   mi.MenuItemID
            WHERE ur.EMAOrganizationID =
                  @EMAOrganizationID
              AND ur.UserID = @UserID
              AND mi.MenuItemCategoryID IS NOT NULL
              AND ISNULL(mi.IsActive, 'Y') = 'Y'
            ORDER BY
                mi.MenuItemCategoryID;
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters.Add(
            "@UserID",
            SqlDbType.Int)
            .Value = userID;

        command.Parameters.Add(
            "@EMAOrganizationID",
            SqlDbType.Int)
            .Value = EMAOrganizationID;

        await using var reader =
            await command.ExecuteReaderAsync();

        var categoryIDs =
            new List<int>();

        while (await reader.ReadAsync())
        {
            categoryIDs.Add(
                Convert.ToInt32(
                    reader["MenuItemCategoryID"]));
        }

        if (!categoryIDs.Contains(
                BrochureCategoryID))
        {
            categoryIDs.Add(
                BrochureCategoryID);
        }

        return categoryIDs
            .Distinct()
            .OrderBy(categoryID =>
                categoryID)
            .ToList();
    }
}
