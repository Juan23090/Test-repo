using System.Text.Json;
using EMA.Api.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Authentication;

public sealed class DonorLogin
{
    private const int BrochureCategoryID = 10;
    private const int DonorsCategoryID = 30;

    private readonly ILogger<DonorLogin> _logger;
    private readonly string _connectionString;

    public DonorLogin(
        ILogger<DonorLogin> logger)
    {
        _logger = logger;

        _connectionString =
            Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString.");
    }

    [Function("DonorLogin")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,             "post",
            Route = "donor-login")]
        HttpRequest request)
    {
        if (!ApiRuntimeEnvironment
                .IsDevelopment)
        {
            return new NotFoundResult();
        }

        DonorLoginRequestDto? loginRequest;

        try
        {
            loginRequest =
                await JsonSerializer.DeserializeAsync<DonorLoginRequestDto>(
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
                message = "Invalid donor login request."
            });
        }

        if (loginRequest is null ||
            loginRequest.EMAOrganizationID <= 0 ||
            loginRequest.DonorID <= 0)
        {
            return new BadRequestObjectResult(new
            {
                message =
                    "EMAOrganizationID and DonorID are required."
            });
        }

        _logger.LogInformation(
            "Simple donor login requested for organization {EMAOrganizationID}, donor {DonorID}.",
            loginRequest.EMAOrganizationID,
            loginRequest.DonorID);

        try
        {
            await using var connection =
                new SqlConnection(
                    _connectionString);

            await connection.OpenAsync();

            const string sql = """
                SELECT DISTINCT
                    d.EMADonorID,
                    d.EMAOrganizationID,
                    d.OfficialDonorName,
                    d.InteracDonorName,
                    d.EmailAddress,
                    d.PhoneNumber,
                    o.OrganizationName,
                    r.RoleID,
                    r.RoleName,
                    mi.MenuItemCategoryID
                FROM dbo.Donor d
                INNER JOIN dbo.Organization o
                    ON d.EMAOrganizationID =
                       o.EMAOrganizationID
                INNER JOIN dbo.[Role] r
                    ON o.DonorRoleID =
                       r.RoleID
                LEFT JOIN dbo.RoleMenuItem rmi
                    ON r.RoleID =
                       rmi.RoleID
                LEFT JOIN dbo.MenuItem mi
                    ON rmi.MenuItemID =
                       mi.MenuItemID
                   AND mi.IsActive = 'Y'
                WHERE d.EMADonorID = @DonorID
                  AND d.EMAOrganizationID = @EMAOrganizationID
                  AND r.IsActive = 'Y'
                ORDER BY
                    r.RoleID,
                    mi.MenuItemCategoryID;
                """;

            await using var command =
                new SqlCommand(
                    sql,
                    connection);

            command.Parameters.AddWithValue(
                "@EMAOrganizationID",
                loginRequest.EMAOrganizationID);

            command.Parameters.AddWithValue(
                "@DonorID",
                loginRequest.DonorID);

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
                            loginRequest.DonorID,

                        DonorID =
                            loginRequest.DonorID,

                        IsDonorSession =
                            true,

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
                            Convert.ToInt32(
                                reader["EMAOrganizationID"]),

                        OrganizationName =
                            reader["OrganizationName"]?.ToString()
                            ?? string.Empty
                    };

                var roleID =
                    Convert.ToInt32(
                        reader["RoleID"]);

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
                        "The donor was not found for this organization, or the organization does not have an active donor role."
                });
            }

            loginResponse.Roles =
                loginResponse.Roles
                    .OrderBy(role => role.RoleName)
                    .ToList();

            loginResponse.AllowedCategoryIDs.Add(
                BrochureCategoryID);

            if (!menuCategoryIDs.Contains(
                    DonorsCategoryID))
            {
                return Forbidden(
                    "This donor role does not have Donors menu access.");
            }

            loginResponse.AllowedCategoryIDs.Add(
                DonorsCategoryID);

            loginResponse.DefaultCategoryID =
                DonorsCategoryID;

            return new OkObjectResult(
                loginResponse);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error logging in donor {DonorID} for organization {EMAOrganizationID}.",
                loginRequest.DonorID,
                loginRequest.EMAOrganizationID);

            return new ObjectResult(new
            {
                message =
                    "Unable to complete donor login."
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
            StatusCode =
                StatusCodes.Status403Forbidden
        };
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
}
