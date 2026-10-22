using EMA.Api.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Authentication;

/// <summary>
/// Returns real database organizations, active users, and their active roles
/// for the temporary passwordless login screen.
/// </summary>
public sealed class GetLoginOptions
{
    private readonly ILogger<GetLoginOptions> _logger;
    private readonly string _connectionString;

    public GetLoginOptions(ILogger<GetLoginOptions> logger)
    {
        _logger = logger;

        _connectionString =
            Environment.GetEnvironmentVariable("SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString.");
    }

    [Function("GetLoginOptions")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,             "get",
            Route = "login/options")]
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

        _logger.LogInformation("GetLoginOptions triggered.");

        try
        {
            var organizations =
                new Dictionary<int, LoginOrganizationOptionDto>();

            var users =
                new Dictionary<
                    (int EMAOrganizationID, int UserID),
                    LoginUserOptionDto>();

            await using var connection =
                new SqlConnection(_connectionString);

            await connection.OpenAsync();

            const string sql = """
                SELECT DISTINCT
                    o.EMAOrganizationID,
                    o.OrganizationName,
                    u.UserID,
                    u.UserName,
                    u.EmailAddress,
                    r.RoleID,
                    r.RoleName
                FROM [dbo].[Organization] o
                INNER JOIN [dbo].[OrganizationUser] ou
                    ON o.EMAOrganizationID = ou.EMAOrganizationID
                INNER JOIN [dbo].[User] u
                    ON ou.UserID = u.UserID
                INNER JOIN [dbo].[OrganizationUserRole] ur
                    ON u.UserID = ur.UserID
                   AND ou.EMAOrganizationID =
                       ur.EMAOrganizationID
                INNER JOIN [dbo].[Role] r
                    ON ur.RoleID = r.RoleID
                WHERE u.IsActive = 1
                  AND r.IsActive = 'Y'
                ORDER BY
                    o.OrganizationName,
                    u.UserName,
                    r.RoleName;
                """;

            await using var command =
                new SqlCommand(sql, connection);

            await using var reader =
                await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var EMAOrganizationID =
                    Convert.ToInt32(
                        reader["EMAOrganizationID"]);

                var userID =
                    Convert.ToInt32(reader["UserID"]);

                if (!organizations.TryGetValue(
                        EMAOrganizationID,
                        out var organization))
                {
                    organization =
                        new LoginOrganizationOptionDto
                        {
                            EMAOrganizationID = EMAOrganizationID,

                            OrganizationName =
                                reader["OrganizationName"]?.ToString()
                                ?? $"Organization {EMAOrganizationID}"
                        };

                    organizations[EMAOrganizationID] =
                        organization;
                }

                var userKey =
                    (EMAOrganizationID, userID);

                if (!users.TryGetValue(
                        userKey,
                        out var user))
                {
                    user = new LoginUserOptionDto
                    {
                        UserID = userID,

                        UserName =
                            reader["UserName"]?.ToString()
                            ?? string.Empty,

                        EmailAddress =
                            reader["EmailAddress"]?.ToString()
                            ?? string.Empty
                    };

                    users[userKey] = user;
                    organization.Users.Add(user);
                }

                var roleID =
                    Convert.ToInt32(reader["RoleID"]);

                if (user.Roles.All(
                        role => role.RoleID != roleID))
                {
                    user.Roles.Add(new LoginRoleDto
                    {
                        RoleID = roleID,

                        RoleName =
                            reader["RoleName"]?.ToString()
                            ?? string.Empty
                    });
                }
            }

            foreach (var organization in
                     organizations.Values)
            {
                organization.Users =
                    organization.Users
                        .OrderBy(user => user.UserName)
                        .ToList();

                foreach (var user in organization.Users)
                {
                    user.Roles =
                        user.Roles
                            .OrderBy(role => role.RoleName)
                            .ToList();
                }
            }

            var response =
                new LoginOptionsResponseDto
                {
                    Organizations =
                        organizations.Values
                            .OrderBy(item =>
                                item.OrganizationName)
                            .ToList()
                };

            return new OkObjectResult(response);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error loading login options.");

            return new ObjectResult(new
            {
                message = "Unable to load login options."
            })
            {
                StatusCode =
                    StatusCodes.Status500InternalServerError
            };
        }
    }

    private static bool IsPasswordlessLoginEnabled()
    {
        return ApiRuntimeEnvironment
            .IsDevelopment;
    }
}
