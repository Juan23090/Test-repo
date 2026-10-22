using System.Data;
using System.Net.Mail;
using System.Text.Json;
using EMA.Api.Services.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Users;

public sealed class SaveUser
{
    private const int MaximumRolesPerUser = 20;

    private const int InsiderRoleID = 9000;

    private readonly ILogger<SaveUser> _logger;
    private readonly string _connectionString;
    private readonly ApiRequestBoundaryValidator
        _authorization;

    public SaveUser(
        ILogger<SaveUser> logger,
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

    [Function("SaveUser")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
                        "post",
            Route =
                "organization/{orgID:int}/users/save")]
        HttpRequest request,
        int orgID)
    {
        UserDto? user;

        try
        {
            user =
                await JsonSerializer
                    .DeserializeAsync<UserDto>(
                        request.Body,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive =
                                true
                        },
                        request.HttpContext
                            .RequestAborted);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult(
                new
                {
                    message =
                        "The user payload is invalid."
                });
        }

        var validationMessage =
            Validate(
                user);

        if (validationMessage is not null)
        {
            return new BadRequestObjectResult(
                new
                {
                    message =
                        validationMessage
                });
        }

        var identity =
            request
                .GetRequiredApiIdentity();

        var roleIDs =
            user!.RoleIDs
                .Distinct()
                .OrderBy(
                    roleID =>
                        roleID)
                .ToArray();

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

        try
        {
            var isPlatformAdministrator =
                _authorization
                    .IsPlatformAdministrator(
                        identity);

            if (!isPlatformAdministrator &&
                user.UserID > 0 &&
                await HasPlatformAccessAsync(
                    connection,
                    transaction,
                    orgID,
                    user.UserID,
                    _authorization
                        .PlatformAdministratorCategoryID,
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
                            "Only a platform administrator can change this account."
                    })
                {
                    StatusCode =
                        StatusCodes.Status403Forbidden
                };
            }

            if (!await AreRolesAssignableAsync(
                    connection,
                    transaction,
                    roleIDs,
                    orgID,
                    _authorization
                        .PlatformOrganizationID,
                    isPlatformAdministrator,
                    _authorization
                        .PlatformAdministratorCategoryID,
                    request.HttpContext
                        .RequestAborted))
            {
                await transaction.RollbackAsync(
                    request.HttpContext
                        .RequestAborted);

                return new BadRequestObjectResult(
                    new
                    {
                        message =
                            "One or more selected roles are not available."
                    });
            }

            var userID =
                user.UserID;

            if (userID == 0)
            {
                userID =
                    await CreateUserAsync(
                        connection,
                        transaction,
                        user,
                        orgID,
                        request.HttpContext
                            .RequestAborted);
            }
            else
            {
                var updateResult =
                    await UpdateUserAsync(
                        connection,
                        transaction,
                        user,
                        orgID,
                        identity.UserID,
                        request.HttpContext
                            .RequestAborted);

                if (updateResult ==
                    UserUpdateResult.NotFound)
                {
                    await transaction.RollbackAsync(
                        request.HttpContext
                            .RequestAborted);

                    return new NotFoundObjectResult(
                        new
                        {
                            message =
                                "The user was not found in this organization."
                        });
                }

                if (updateResult ==
                    UserUpdateResult.SharedIdentity)
                {
                    await transaction.RollbackAsync(
                        request.HttpContext
                            .RequestAborted);

                    return new ObjectResult(
                        new
                        {
                            message =
                                "This account belongs to more than one organization. Its shared profile can only be changed by that user."
                        })
                    {
                        StatusCode =
                            StatusCodes
                                .Status409Conflict
                    };
                }
            }

            await ReplaceRolesAsync(
                connection,
                transaction,
                orgID,
                userID,
                roleIDs,
                request.HttpContext
                    .RequestAborted);

            await RevokeUserSessionsAsync(
                connection,
                transaction,
                orgID,
                userID,
                request.HttpContext
                    .RequestAborted);

            await transaction.CommitAsync(
                request.HttpContext
                    .RequestAborted);

            _logger.LogInformation(
                "User {TargetUserID} saved in organization {EMAOrganizationID} by user {ActorUserID}.",
                userID,
                orgID,
                identity.UserID);

            return new OkObjectResult(
                new
                {
                    UserID =
                        userID
                });
        }
        catch (SqlException exception)
        {
            await transaction.RollbackAsync(
                request.HttpContext
                    .RequestAborted);

            _logger.LogError(
                exception,
                "Unable to save a user in organization {EMAOrganizationID}.",
                orgID);

            return new ObjectResult(
                new
                {
                    message =
                        "Unable to save the user."
                })
            {
                StatusCode =
                    StatusCodes
                        .Status500InternalServerError
            };
        }
    }

    private static string? Validate(
        UserDto? user)
    {
        if (user is null)
        {
            return "User information is required.";
        }

        user.UserName =
            user.UserName?.Trim()
            ?? string.Empty;

        user.EmailAddress =
            user.EmailAddress?.Trim()
            ?? string.Empty;

        user.PhoneNumber =
            user.PhoneNumber?.Trim()
            ?? string.Empty;

        if (user.UserID < 0 ||
            user.UserName.Length is < 1 or > 200)
        {
            return "A valid user name is required.";
        }

        if (user.EmailAddress.Length > 320 ||
            !MailAddress.TryCreate(
                user.EmailAddress,
                out var parsedEmailAddress) ||
            !string.Equals(
                parsedEmailAddress.Address,
                user.EmailAddress,
                StringComparison
                    .OrdinalIgnoreCase))
        {
            return "A valid email address is required.";
        }

        if (user.PhoneNumber.Length > 50)
        {
            return "The phone number is too long.";
        }

        if (user.RoleIDs is not
            { Count: > 0 } ||
            user.RoleIDs.Count >
                MaximumRolesPerUser ||
            user.RoleIDs.Any(
                roleID =>
                    roleID <= 0))
        {
            return "Between 1 and 20 valid roles must be selected.";
        }

        return null;
    }

    private static async Task<bool>
        AreRolesAssignableAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        IReadOnlyList<int> roleIDs,
        int organizationID,
        int platformOrganizationID,
        bool isPlatformAdministrator,
        int platformAdministratorCategoryID,
        CancellationToken cancellationToken)
    {
        var parameterNames =
            roleIDs
                .Select(
                    (_, index) =>
                        $"@RoleID{index}")
                .ToArray();

        var sql =
            $"""
             SELECT COUNT(DISTINCT RoleID)
             FROM dbo.[Role] role
             WHERE role.RoleID IN
                   (
                       {string.Join(
                           ", ",
                           parameterNames)}
                   )
               AND ISNULL(role.IsActive, 'Y') = 'Y'
               AND
               (
                   role.RoleID <> @InsiderRoleID
                   OR @EMAOrganizationID =
                      @PlatformOrganizationID
               )
               AND NOT EXISTS
               (
                   SELECT 1
                   FROM dbo.Organization
                       donorRoleOrganization
                   WHERE donorRoleOrganization
                             .EMAOrganizationID =
                         @EMAOrganizationID
                     AND donorRoleOrganization
                             .DonorRoleID =
                         role.RoleID
               )
               AND
               (
                   @IsPlatformAdministrator = 1
                   OR NOT EXISTS
                   (
                       SELECT 1
                       FROM dbo.RoleMenuItem roleMenuItem
                       INNER JOIN dbo.MenuItem menuItem
                           ON menuItem.MenuItemID =
                              roleMenuItem.MenuItemID
                       WHERE roleMenuItem.RoleID =
                             role.RoleID
                         AND menuItem.MenuItemCategoryID =
                             @PlatformAdministratorCategoryID
                         AND ISNULL(
                                 menuItem.IsActive,
                                 'Y') = 'Y'
                   )
               );
             """;

        await using var command =
            new SqlCommand(
                sql,
                connection,
                transaction);

        for (var index = 0;
             index < roleIDs.Count;
             index++)
        {
            command.Parameters.Add(
                parameterNames[index],
                SqlDbType.Int)
                .Value =
                    roleIDs[index];
        }

        command.Parameters.Add(
            "@EMAOrganizationID",
            SqlDbType.Int)
            .Value =
                organizationID;

        command.Parameters.Add(
            "@PlatformOrganizationID",
            SqlDbType.Int)
            .Value =
                platformOrganizationID;

        command.Parameters.Add(
            "@InsiderRoleID",
            SqlDbType.Int)
            .Value =
                InsiderRoleID;

        command.Parameters.Add(
            "@IsPlatformAdministrator",
            SqlDbType.Bit)
            .Value =
                isPlatformAdministrator;

        command.Parameters.Add(
            "@PlatformAdministratorCategoryID",
            SqlDbType.Int)
            .Value =
                platformAdministratorCategoryID;

        var count =
            Convert.ToInt32(
                await command
                    .ExecuteScalarAsync(
                        cancellationToken));

        return count ==
               roleIDs.Count;
    }

    private static async Task<bool>
        HasPlatformAccessAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            int orgID,
            int userID,
            int platformAdministratorCategoryID,
            CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT CASE
                WHEN EXISTS
                (
                    SELECT 1
                    FROM dbo.OrganizationUserRole
                        organizationUserRole
                    INNER JOIN dbo.RoleMenuItem
                        roleMenuItem
                        ON roleMenuItem.RoleID =
                           organizationUserRole.RoleID
                    INNER JOIN dbo.MenuItem menuItem
                        ON menuItem.MenuItemID =
                           roleMenuItem.MenuItemID
                    WHERE organizationUserRole
                              .EMAOrganizationID =
                          @EMAOrganizationID
                      AND organizationUserRole.UserID =
                          @UserID
                      AND menuItem.MenuItemCategoryID =
                          @PlatformAdministratorCategoryID
                      AND ISNULL(
                              menuItem.IsActive,
                              'Y') = 'Y'
                )
                THEN 1
                ELSE 0
            END;
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection,
                transaction);

        AddMembershipParameters(
            command,
            orgID,
            userID);

        command.Parameters.Add(
            "@PlatformAdministratorCategoryID",
            SqlDbType.Int)
            .Value =
                platformAdministratorCategoryID;

        return Convert.ToInt32(
                   await command
                       .ExecuteScalarAsync(
                           cancellationToken)) ==
               1;
    }

    private static async Task<int>
        CreateUserAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            UserDto user,
            int orgID,
            CancellationToken cancellationToken)
    {
        const string createUserSql = """
            INSERT INTO dbo.[User]
            (
                UserName,
                EmailAddress,
                EmailAddressValidated,
                PhoneNumber,
                IsActive,
                DateTimeCreated,
                DateTimeUpdated
            )
            OUTPUT INSERTED.UserID
            VALUES
            (
                @UserName,
                @EmailAddress,
                'N',
                @PhoneNumber,
                @IsActive,
                SYSUTCDATETIME(),
                SYSUTCDATETIME()
            );
            """;

        await using var userCommand =
            new SqlCommand(
                createUserSql,
                connection,
                transaction);

        AddUserParameters(
            userCommand,
            user);

        var userID =
            Convert.ToInt32(
                await userCommand
                    .ExecuteScalarAsync(
                        cancellationToken));

        const string membershipSql = """
            INSERT INTO dbo.OrganizationUser
            (
                EMAOrganizationID,
                UserID,
                OrganizationUserStatusID
            )
            VALUES
            (
                @EMAOrganizationID,
                @UserID,
                10
            );
            """;

        await using var membershipCommand =
            new SqlCommand(
                membershipSql,
                connection,
                transaction);

        membershipCommand.Parameters.Add(
            "@EMAOrganizationID",
            SqlDbType.Int)
            .Value =
                orgID;

        membershipCommand.Parameters.Add(
            "@UserID",
            SqlDbType.Int)
            .Value =
                userID;

        await membershipCommand
            .ExecuteNonQueryAsync(
                cancellationToken);

        return userID;
    }

    private static async Task<
        UserUpdateResult> UpdateUserAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            UserDto user,
            int orgID,
            int actorUserID,
            CancellationToken cancellationToken)
    {
        const string existingSql = """
            SELECT
                appUser.UserName,
                appUser.EmailAddress,
                appUser.PhoneNumber,
                appUser.IsActive,
                (
                    SELECT COUNT_BIG(*)
                    FROM dbo.OrganizationUser
                    WHERE UserID =
                          appUser.UserID
                ) AS MembershipCount
            FROM dbo.[User] appUser
            INNER JOIN dbo.OrganizationUser
                organizationUser
                ON organizationUser.UserID =
                   appUser.UserID
            WHERE appUser.UserID = @UserID
              AND organizationUser
                      .EMAOrganizationID =
                  @EMAOrganizationID;
            """;

        await using var existingCommand =
            new SqlCommand(
                existingSql,
                connection,
                transaction);

        existingCommand.Parameters.Add(
            "@UserID",
            SqlDbType.Int)
            .Value =
                user.UserID;

        existingCommand.Parameters.Add(
            "@EMAOrganizationID",
            SqlDbType.Int)
            .Value =
                orgID;

        await using var reader =
            await existingCommand
                .ExecuteReaderAsync(
                    cancellationToken);

        if (!await reader.ReadAsync(
                cancellationToken))
        {
            return UserUpdateResult
                .NotFound;
        }

        var profileChanged =
            !string.Equals(
                reader["UserName"]
                    ?.ToString(),
                user.UserName,
                StringComparison.Ordinal) ||
            !string.Equals(
                reader["EmailAddress"]
                    ?.ToString(),
                user.EmailAddress,
                StringComparison
                    .OrdinalIgnoreCase) ||
            !string.Equals(
                reader["PhoneNumber"]
                    ?.ToString(),
                user.PhoneNumber,
                StringComparison.Ordinal) ||
            Convert.ToBoolean(
                reader["IsActive"]) !=
            user.IsActive;

        var membershipCount =
            Convert.ToInt64(
                reader[
                    "MembershipCount"]);

        await reader.CloseAsync();

        if (profileChanged &&
            membershipCount > 1 &&
            user.UserID != actorUserID)
        {
            return UserUpdateResult
                .SharedIdentity;
        }

        const string updateSql = """
            UPDATE dbo.[User]
            SET UserName = @UserName,
                EmailAddress = @EmailAddress,
                EmailAddressValidated =
                    CASE
                        WHEN LOWER(
                                 LTRIM(
                                     RTRIM(
                                         EmailAddress))) =
                             LOWER(
                                 LTRIM(
                                     RTRIM(
                                         @EmailAddress)))
                        THEN EmailAddressValidated
                        ELSE 'N'
                    END,
                PhoneNumber = @PhoneNumber,
                IsActive = @IsActive,
                DateTimeUpdated =
                    SYSUTCDATETIME()
            WHERE UserID = @UserID;
            """;

        await using var updateCommand =
            new SqlCommand(
                updateSql,
                connection,
                transaction);

        updateCommand.Parameters.Add(
            "@UserID",
            SqlDbType.Int)
            .Value =
                user.UserID;

        AddUserParameters(
            updateCommand,
            user);

        await updateCommand
            .ExecuteNonQueryAsync(
                cancellationToken);

        return UserUpdateResult
            .Updated;
    }

    private static void AddUserParameters(
        SqlCommand command,
        UserDto user)
    {
        command.Parameters.Add(
            "@UserName",
            SqlDbType.NVarChar,
            200)
            .Value =
                user.UserName;

        command.Parameters.Add(
            "@EmailAddress",
            SqlDbType.NVarChar,
            320)
            .Value =
                user.EmailAddress;

        command.Parameters.Add(
            "@PhoneNumber",
            SqlDbType.NVarChar,
            50)
            .Value =
                user.PhoneNumber;

        command.Parameters.Add(
            "@IsActive",
            SqlDbType.Bit)
            .Value =
                user.IsActive;
    }

    private static async Task ReplaceRolesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int orgID,
        int userID,
        IReadOnlyList<int> roleIDs,
        CancellationToken cancellationToken)
    {
        const string deleteSql = """
            DELETE FROM dbo.OrganizationUserRole
            WHERE EMAOrganizationID =
                  @EMAOrganizationID
              AND UserID = @UserID;
            """;

        await using (var deleteCommand =
                     new SqlCommand(
                         deleteSql,
                         connection,
                         transaction))
        {
            AddMembershipParameters(
                deleteCommand,
                orgID,
                userID);

            await deleteCommand
                .ExecuteNonQueryAsync(
                    cancellationToken);
        }

        const string insertSql = """
            INSERT INTO dbo.OrganizationUserRole
            (
                EMAOrganizationID,
                UserID,
                RoleID
            )
            VALUES
            (
                @EMAOrganizationID,
                @UserID,
                @RoleID
            );
            """;

        foreach (var roleID in roleIDs)
        {
            await using var insertCommand =
                new SqlCommand(
                    insertSql,
                    connection,
                    transaction);

            AddMembershipParameters(
                insertCommand,
                orgID,
                userID);

            insertCommand.Parameters.Add(
                "@RoleID",
                SqlDbType.Int)
                .Value =
                    roleID;

            await insertCommand
                .ExecuteNonQueryAsync(
                    cancellationToken);
        }
    }

    private static async Task
        RevokeUserSessionsAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            int orgID,
            int userID,
            CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE dbo.ApiSession
            SET RevokedAtUtc =
                COALESCE(
                    RevokedAtUtc,
                    SYSUTCDATETIME())
            WHERE EMAOrganizationID =
                  @EMAOrganizationID
              AND UserID = @UserID
              AND ActorType =
                  'OrganizationUser'
              AND RevokedAtUtc IS NULL;
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection,
                transaction);

        AddMembershipParameters(
            command,
            orgID,
            userID);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static void AddMembershipParameters(
        SqlCommand command,
        int orgID,
        int userID)
    {
        command.Parameters.Add(
            "@EMAOrganizationID",
            SqlDbType.Int)
            .Value =
                orgID;

        command.Parameters.Add(
            "@UserID",
            SqlDbType.Int)
            .Value =
                userID;
    }

    public sealed class UserDto
    {
        public int UserID { get; set; }

        public string UserName { get; set; } =
            string.Empty;

        public string EmailAddress { get; set; } =
            string.Empty;

        public string PhoneNumber { get; set; } =
            string.Empty;

        public bool IsActive { get; set; }

        public List<int> RoleIDs { get; set; } =
            new();
    }

    private enum UserUpdateResult
    {
        Updated,
        NotFound,
        SharedIdentity
    }
}
