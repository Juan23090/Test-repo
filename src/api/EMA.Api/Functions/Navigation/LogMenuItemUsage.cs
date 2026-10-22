using System.Data;
using System.Text.Json;
using EMA.Api.Services.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Navigation;

public sealed class LogMenuItemUsage
{
    private readonly ILogger<LogMenuItemUsage>
        _logger;

    private readonly ApiRequestBoundaryValidator
        _authorization;

    private readonly string
        _connectionString;

    public LogMenuItemUsage(
        ILogger<LogMenuItemUsage> logger,
        ApiRequestBoundaryValidator authorization)
    {
        _logger =
            logger;

        _authorization =
            authorization;

        _connectionString =
            Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString.");
    }

    [Function("LogMenuItemUsage")]
    public async Task<IActionResult> RunAsync(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "post",
            Route = "navigation/menu-item-usage")]
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var identity =
            request.GetRequiredApiIdentity();

        MenuItemUsageRequest? payload;

        try
        {
            payload =
                await JsonSerializer.DeserializeAsync<MenuItemUsageRequest>(
                    request.Body,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    },
                    cancellationToken);
        }
        catch (JsonException)
        {
            return InvalidMenuItem();
        }

        if (payload?.MenuItemID is not > 0)
        {
            return InvalidMenuItem();
        }

        await using var connection =
            new SqlConnection(
                _connectionString);

        await connection.OpenAsync(
            cancellationToken);

        var isAdministrator =
            _authorization.IsOrganizationAdministrator(
                identity);

        var categoryID =
            await GetAccessibleMenuCategoryIDAsync(
                connection,
                identity,
                payload.MenuItemID.Value,
                isAdministrator,
                cancellationToken);

        if (!categoryID.HasValue)
        {
            return new NotFoundObjectResult(
                new
                {
                    Message =
                        "The active menu item was not found."
                });
        }

        if (!identity.AllowedCategoryIDs.Contains(
                categoryID.Value))
        {
            return new ObjectResult(
                new
                {
                    Message =
                        "The current session cannot access that menu category."
                })
            {
                StatusCode =
                    StatusCodes.Status403Forbidden
            };
        }

        const string sql = """
            INSERT INTO dbo.MenuItemUsage
            (
                ApiSessionID,
                MenuItemID,
                UserID,
                DateTimeLogged
            )
            SELECT
                apiSession.ApiSessionID,
                @MenuItemID,
                CASE
                    WHEN apiSession.ActorType = 'OrganizationUser'
                        THEN apiSession.UserID
                    ELSE NULL
                END,
                SYSUTCDATETIME()
            FROM dbo.ApiSession apiSession
            WHERE apiSession.ApiSessionID =
                      @ApiSessionID
              AND apiSession.SecurityStamp =
                      @SecurityStamp
              AND apiSession.EMAOrganizationID =
                      @EMAOrganizationID
              AND apiSession.ActorType =
                      @ActorType
              AND apiSession.RevokedAtUtc IS NULL
              AND apiSession.ExpiresAtUtc >
                      SYSUTCDATETIME();
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters.Add(
            "@ApiSessionID",
            SqlDbType.UniqueIdentifier)
            .Value = identity.SessionID;

        command.Parameters.Add(
            "@SecurityStamp",
            SqlDbType.UniqueIdentifier)
            .Value = identity.SecurityStamp;

        command.Parameters.Add(
            "@EMAOrganizationID",
            SqlDbType.Int)
            .Value = identity.EMAOrganizationID;

        command.Parameters.Add(
            "@ActorType",
            SqlDbType.VarChar,
            32)
            .Value = identity.ActorType.ToString();

        command.Parameters.Add(
            "@MenuItemID",
            SqlDbType.Int)
            .Value = payload.MenuItemID.Value;

        var rows =
            await command.ExecuteNonQueryAsync(
                cancellationToken);

        if (rows == 0)
        {
            return new UnauthorizedObjectResult(
                new
                {
                    Message =
                        "The active API session was not found."
                });
        }

        _logger.LogInformation(
            "Logged menu item {MenuItemID} for API session {ApiSessionID}.",
            payload.MenuItemID.Value,
            identity.SessionID);

        return new NoContentResult();
    }

    private static async Task<int?>
        GetAccessibleMenuCategoryIDAsync(
            SqlConnection connection,
            ApiRequestIdentity identity,
            int menuItemID,
            bool isAdministrator,
            CancellationToken cancellationToken)
    {
        var sql =
            isAdministrator
                ? """
                  SELECT TOP (1)
                      menuItem.MenuItemCategoryID
                  FROM dbo.MenuItem menuItem
                  WHERE menuItem.MenuItemID = @MenuItemID
                    AND menuItem.IsActive = 'Y'
                    AND menuItem.MenuItemCategoryID IS NOT NULL
                    AND
                    (
                        menuItem.MenuItemCategoryID = 10
                        OR EXISTS
                        (
                            SELECT 1
                            FROM dbo.MenuItem categoryMenuItem
                            INNER JOIN dbo.RoleMenuItem categoryRoleMenuItem
                                ON categoryRoleMenuItem.MenuItemID = categoryMenuItem.MenuItemID
                            INNER JOIN dbo.OrganizationUserRole categoryUserRole
                                ON categoryUserRole.RoleID = categoryRoleMenuItem.RoleID
                               AND categoryUserRole.EMAOrganizationID = @EMAOrganizationID
                            INNER JOIN dbo.OrganizationUser categoryOrganizationUser
                                ON categoryOrganizationUser.UserID = categoryUserRole.UserID
                               AND categoryOrganizationUser.EMAOrganizationID = @EMAOrganizationID
                            INNER JOIN dbo.[User] categoryAppUser
                                ON categoryAppUser.UserID = categoryUserRole.UserID
                            INNER JOIN dbo.[Role] categoryRole
                                ON categoryRole.RoleID = categoryUserRole.RoleID
                            WHERE categoryMenuItem.MenuItemCategoryID = menuItem.MenuItemCategoryID
                              AND categoryMenuItem.IsActive = 'Y'
                              AND categoryUserRole.UserID = @UserID
                              AND categoryAppUser.IsActive = 1
                              AND categoryRole.IsActive = 'Y'
                        )
                    );
                  """
                : identity.ActorType ==
                  ApiActorType.OrganizationUser
                    ? """
                      SELECT TOP (1)
                          menuItem.MenuItemCategoryID
                      FROM dbo.MenuItem menuItem
                      WHERE menuItem.MenuItemID = @MenuItemID
                        AND menuItem.IsActive = 'Y'
                        AND menuItem.MenuItemCategoryID IS NOT NULL
                        AND
                        (
                            menuItem.MenuItemCategoryID = 10
                            OR EXISTS
                            (
                                SELECT 1
                                FROM dbo.RoleMenuItem roleMenuItem
                                INNER JOIN dbo.OrganizationUserRole userRole
                                    ON userRole.RoleID = roleMenuItem.RoleID
                                   AND userRole.EMAOrganizationID = @EMAOrganizationID
                                INNER JOIN dbo.OrganizationUser organizationUser
                                    ON organizationUser.UserID = userRole.UserID
                                   AND organizationUser.EMAOrganizationID = @EMAOrganizationID
                                INNER JOIN dbo.[User] appUser
                                    ON appUser.UserID = userRole.UserID
                                INNER JOIN dbo.[Role] roleDefinition
                                    ON roleDefinition.RoleID = userRole.RoleID
                                WHERE roleMenuItem.MenuItemID = menuItem.MenuItemID
                                  AND userRole.UserID = @UserID
                                  AND organizationUser.OrganizationUserStatusID = 10
                                  AND appUser.IsActive = 1
                                  AND roleDefinition.IsActive = 'Y'
                            )
                        );
                      """
                    : """
                  SELECT TOP (1)
                      menuItem.MenuItemCategoryID
                  FROM dbo.MenuItem menuItem
                  WHERE menuItem.MenuItemID = @MenuItemID
                    AND menuItem.IsActive = 'Y'
                    AND menuItem.MenuItemCategoryID IS NOT NULL
                    AND
                    (
                        menuItem.MenuItemCategoryID = 10
                        OR EXISTS
                        (
                            SELECT 1
                            FROM dbo.Donor donor
                            INNER JOIN dbo.Organization organization
                                ON organization.EMAOrganizationID = donor.EMAOrganizationID
                            INNER JOIN dbo.[Role] roleDefinition
                                ON roleDefinition.RoleID = organization.DonorRoleID
                            INNER JOIN dbo.RoleMenuItem roleMenuItem
                                ON roleMenuItem.RoleID = roleDefinition.RoleID
                            WHERE donor.EMADonorID = @DonorID
                              AND donor.EMAOrganizationID = @EMAOrganizationID
                              AND roleMenuItem.MenuItemID = menuItem.MenuItemID
                              AND roleDefinition.IsActive = 'Y'
                        )
                    );
                  """;

        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters.Add(
            "@MenuItemID",
            SqlDbType.Int)
            .Value = menuItemID;

        command.Parameters.Add(
            "@EMAOrganizationID",
            SqlDbType.Int)
            .Value = identity.EMAOrganizationID;

        if (identity.ActorType ==
            ApiActorType.OrganizationUser)
        {
            command.Parameters.Add(
                "@UserID",
                SqlDbType.Int)
                .Value = identity.UserID;
        }
        else
        {
            command.Parameters.Add(
                "@DonorID",
                SqlDbType.Int)
                .Value = identity.DonorID!.Value;
        }

        var result =
            await command.ExecuteScalarAsync(
                cancellationToken);

        return result is null ||
               result == DBNull.Value
            ? null
            : Convert.ToInt32(
                result);
    }

    private static BadRequestObjectResult
        InvalidMenuItem()
    {
        return new BadRequestObjectResult(
            new
            {
                Message =
                    "A valid menu item is required."
            });
    }

    private sealed class MenuItemUsageRequest
    {
        public int? MenuItemID { get; set; }
    }
}
