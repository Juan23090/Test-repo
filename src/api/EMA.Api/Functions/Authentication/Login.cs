using System.Text.Json;
using EMA.Api.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Authentication;

/// <summary>
/// Temporary passwordless login using real database users,
/// organizations, roles, and menu-category permissions.
///
/// Real authentication can later replace the identity-verification
/// portion while preserving this response contract.
/// </summary>
public sealed class Login
{
    private const int BrochureCategoryID = 10;
    private const int OperationsCategoryID = 20;
    private const int DonorsCategoryID = 30;
    private const int SupportCategoryID = 90;

    private readonly ILogger<Login> _logger;
    private readonly string _connectionString;
    private readonly int _supportOrganizationID;

    public Login(ILogger<Login> logger)
    {
        _logger = logger;

        _connectionString =
            Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString.");

        _supportOrganizationID =
            RequiredSecurityConfiguration
                .GetPositiveInt(
                    "PLATFORM_ORGANIZATION_ID");
    }

    [Function("Login")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,             "post",
            Route = "login")]
        HttpRequest request)
    {
        if (!IsPasswordlessLoginEnabled())
        {
            return new ObjectResult(new
            {
                message = "Passwordless login is disabled."
            })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }

        LoginRequestDto? loginRequest;

        try
        {
            loginRequest =
                await JsonSerializer.DeserializeAsync<LoginRequestDto>(
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
                message = "Invalid login request."
            });
        }

        if (loginRequest is null ||
            loginRequest.EMAOrganizationID <= 0 ||
            loginRequest.UserID <= 0)
        {
            return new BadRequestObjectResult(new
            {
                message =
                    "EMAOrganizationID and UserID are required."
            });
        }

        _logger.LogInformation(
            "Passwordless login requested for organization {EMAOrganizationID}, user {UserID}.",
            loginRequest.EMAOrganizationID,
            loginRequest.UserID);

        try
        {
            await using var connection =
                new SqlConnection(_connectionString);

            await connection.OpenAsync();

            const string sql = """
                SELECT DISTINCT
                    u.UserID,
                    u.UserName,
                    u.EmailAddress,
                    u.PhoneNumber,
                    o.EMAOrganizationID,
                    o.OrganizationName,
                    r.RoleID,
                    r.RoleName,
                    mi.MenuItemCategoryID
                FROM [dbo].[User] u
                INNER JOIN [dbo].[OrganizationUser] ou
                    ON u.UserID = ou.UserID
                INNER JOIN [dbo].[Organization] o
                    ON ou.EMAOrganizationID =
                       o.EMAOrganizationID
                INNER JOIN [dbo].[OrganizationUserRole] ur
                    ON u.UserID = ur.UserID
                   AND ou.EMAOrganizationID =
                       ur.EMAOrganizationID
                INNER JOIN [dbo].[Role] r
                    ON ur.RoleID = r.RoleID
                LEFT JOIN [dbo].[RoleMenuItem] rmi
                    ON r.RoleID = rmi.RoleID
                LEFT JOIN [dbo].[MenuItem] mi
                    ON rmi.MenuItemID = mi.MenuItemID
                   AND mi.IsActive = 'Y'
                WHERE o.EMAOrganizationID =
                      @EMAOrganizationID
                  AND u.UserID = @UserID
                  AND u.IsActive = 1
                  AND r.IsActive = 'Y'
                ORDER BY
                    r.RoleID,
                    mi.MenuItemCategoryID;
                """;

            await using var command =
                new SqlCommand(sql, connection);

            command.Parameters.AddWithValue(
                "@EMAOrganizationID",
                loginRequest.EMAOrganizationID);

            command.Parameters.AddWithValue(
                "@UserID",
                loginRequest.UserID);

            await using var reader =
                await command.ExecuteReaderAsync();

            LoginResponseDto? loginResponse = null;

            var menuCategoryIDs =
                new HashSet<int>();

            while (await reader.ReadAsync())
            {
                loginResponse ??=
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
                            ?? string.Empty
                    };

                var roleID =
                    Convert.ToInt32(reader["RoleID"]);

                if (loginResponse.Roles.All(
                        role => role.RoleID != roleID))
                {
                    loginResponse.Roles.Add(
                        new LoginRoleDto
                        {
                            RoleID = roleID,

                            RoleName =
                                reader["RoleName"]?.ToString()
                                ?? string.Empty
                        });
                }

                if (reader["MenuItemCategoryID"] !=
                    DBNull.Value)
                {
                    menuCategoryIDs.Add(
                        Convert.ToInt32(
                            reader["MenuItemCategoryID"]));
                }
            }

            if (loginResponse is null)
            {
                return new UnauthorizedObjectResult(new
                {
                    message =
                        "The selected user is inactive, does not belong to the organization, or has no active roles."
                });
            }

            loginResponse.Roles =
                loginResponse.Roles
                    .OrderBy(role => role.RoleName)
                    .ToList();

            // Every valid session may access the public Brochure category.
            loginResponse.AllowedCategoryIDs.Add(
                BrochureCategoryID);

            if (loginResponse.EMAOrganizationID ==
                _supportOrganizationID)
            {
                if (!menuCategoryIDs.Contains(
                        SupportCategoryID))
                {
                    return Forbidden(
                        "This organization user does not have Support access.");
                }

                loginResponse.AllowedCategoryIDs.Add(
                    SupportCategoryID);

                loginResponse.DefaultCategoryID =
                    SupportCategoryID;
            }
            else
            {
                if (!menuCategoryIDs.Contains(
                        OperationsCategoryID))
                {
                    return Forbidden(
                        "This organization user does not have Operations access.");
                }

                loginResponse.AllowedCategoryIDs.Add(
                    OperationsCategoryID);

                if (menuCategoryIDs.Contains(
                        DonorsCategoryID))
                {
                    loginResponse.AllowedCategoryIDs.Add(
                        DonorsCategoryID);
                }

                loginResponse.DefaultCategoryID =
                    OperationsCategoryID;
            }

            return new OkObjectResult(loginResponse);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error logging in user {UserID} for organization {EMAOrganizationID}.",
                loginRequest.UserID,
                loginRequest.EMAOrganizationID);

            return new ObjectResult(new
            {
                message = "Unable to complete login."
            })
            {
                StatusCode =
                    StatusCodes.Status500InternalServerError
            };
        }
    }

    private static ObjectResult Forbidden(
        string message)
    {
        return new ObjectResult(new
        {
            message
        })
        {
            StatusCode = StatusCodes.Status403Forbidden
        };
    }

    private static bool IsPasswordlessLoginEnabled()
    {
        return ApiRuntimeEnvironment
            .IsDevelopment;
    }
}
