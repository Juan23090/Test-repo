using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace EMA.Api.Services.Security;

public sealed class SqlScreenFeedbackAuthorizationService :
    IScreenFeedbackAuthorizationService
{
    private const int BrochureCategoryID = 10;
    private const int ActiveOrganizationUserStatusID = 10;

    private readonly string _connectionString;
    private readonly ApiRequestBoundaryValidator _boundaryValidator;

    public SqlScreenFeedbackAuthorizationService(
        IConfiguration configuration,
        ApiRequestBoundaryValidator boundaryValidator)
    {
        _boundaryValidator = boundaryValidator;
        _connectionString =
            configuration["SqlConnectionString"]
            ?? Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString.");
    }

    public async Task<bool> CanAccessMenuItemAsync(
        ApiRequestIdentity identity,
        int menuItemID,
        CancellationToken cancellationToken = default)
    {
        if (!IsOrganizationUser(identity) || menuItemID <= 0)
        {
            return false;
        }

        await using var connection =
            await OpenConnectionAsync(cancellationToken);

        const string sql = """
            SELECT CASE WHEN EXISTS
            (
                SELECT 1
                FROM dbo.MenuItem AS menuItem
                INNER JOIN dbo.OrganizationUser AS organizationUser
                    ON organizationUser.UserID = @UserID
                   AND organizationUser.EMAOrganizationID = @OrganizationID
                INNER JOIN dbo.[User] AS appUser
                    ON appUser.UserID = organizationUser.UserID
                WHERE menuItem.MenuItemID = @MenuItemID
                  AND menuItem.IsActive = 'Y'
                  AND organizationUser.OrganizationUserStatusID = @ActiveOrganizationUserStatusID
                  AND appUser.IsActive = 1
                  AND
                  (
                      menuItem.MenuItemCategoryID = @BrochureCategoryID
                      OR
                      (
                          @IsAdministrator = 1
                          AND EXISTS
                          (
                              SELECT 1
                              FROM dbo.OrganizationUserRole AS categoryUserRole
                              INNER JOIN dbo.RoleMenuItem AS categoryRoleMenuItem
                                  ON categoryRoleMenuItem.RoleID = categoryUserRole.RoleID
                              INNER JOIN dbo.MenuItem AS categoryMenuItem
                                  ON categoryMenuItem.MenuItemID = categoryRoleMenuItem.MenuItemID
                              INNER JOIN dbo.[Role] AS categoryRoleDefinition
                                  ON categoryRoleDefinition.RoleID = categoryUserRole.RoleID
                              WHERE categoryUserRole.EMAOrganizationID = @OrganizationID
                                AND categoryUserRole.UserID = @UserID
                                AND categoryMenuItem.MenuItemCategoryID = menuItem.MenuItemCategoryID
                                AND categoryMenuItem.IsActive = 'Y'
                                AND categoryRoleDefinition.IsActive = 'Y'
                          )
                      )
                      OR EXISTS
                      (
                          SELECT 1
                          FROM dbo.RoleMenuItem AS roleMenuItem
                          INNER JOIN dbo.OrganizationUserRole AS organizationUserRole
                              ON organizationUserRole.RoleID = roleMenuItem.RoleID
                             AND organizationUserRole.EMAOrganizationID = @OrganizationID
                             AND organizationUserRole.UserID = @UserID
                          INNER JOIN dbo.[Role] AS roleDefinition
                              ON roleDefinition.RoleID = organizationUserRole.RoleID
                          WHERE roleMenuItem.MenuItemID = menuItem.MenuItemID
                            AND roleDefinition.IsActive = 'Y'
                      )
                  )
            ) THEN 1 ELSE 0 END;
            """;

        await using var command = new SqlCommand(sql, connection);
        AddOrganizationUserParameters(command, identity);
        command.Parameters.Add("@MenuItemID", SqlDbType.Int).Value = menuItemID;
        command.Parameters.Add("@BrochureCategoryID", SqlDbType.Int).Value = BrochureCategoryID;
        command.Parameters.Add("@IsAdministrator", SqlDbType.Bit).Value =
            _boundaryValidator.IsOrganizationAdministrator(identity);
        command.Parameters.Add("@ActiveOrganizationUserStatusID", SqlDbType.Int).Value = ActiveOrganizationUserStatusID;

        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    public async Task<bool> CanAccessEnhancementRequestAsync(
        ApiRequestIdentity identity,
        int enhancementRequestID,
        CancellationToken cancellationToken = default)
    {
        if (!IsOrganizationUser(identity) || enhancementRequestID <= 0)
        {
            return false;
        }

        await using var connection =
            await OpenConnectionAsync(cancellationToken);

        const string sql = """
            SELECT TOP (1)
                request.MenuItemID
            FROM dbo.EnhancementRequest AS request
            WHERE request.EnhancementRequestID = @EnhancementRequestID;
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@EnhancementRequestID", SqlDbType.Int).Value = enhancementRequestID;

        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is null || value is DBNull)
        {
            return false;
        }

        return await CanAccessMenuItemAsync(
            identity,
            Convert.ToInt32(value),
            cancellationToken);
    }

    public async Task<bool> CanAccessInsiderRouteAsync(
        ApiRequestIdentity identity,
        string route,
        CancellationToken cancellationToken = default)
    {
        if (!IsOrganizationUser(identity) ||
            string.IsNullOrWhiteSpace(route))
        {
            return false;
        }

        await using var connection =
            await OpenConnectionAsync(cancellationToken);

        const string sql = """
            SELECT CASE WHEN EXISTS
            (
                SELECT 1
                FROM dbo.MenuItem AS menuItem
                INNER JOIN dbo.RoleMenuItem AS roleMenuItem
                    ON roleMenuItem.MenuItemID = menuItem.MenuItemID
                INNER JOIN dbo.OrganizationUserRole AS organizationUserRole
                    ON organizationUserRole.RoleID = roleMenuItem.RoleID
                   AND organizationUserRole.EMAOrganizationID = @PlatformOrganizationID
                   AND organizationUserRole.UserID = @UserID
                INNER JOIN dbo.OrganizationUser AS organizationUser
                    ON organizationUser.UserID = organizationUserRole.UserID
                   AND organizationUser.EMAOrganizationID = @PlatformOrganizationID
                INNER JOIN dbo.[User] AS appUser
                    ON appUser.UserID = organizationUserRole.UserID
                INNER JOIN dbo.[Role] AS roleDefinition
                    ON roleDefinition.RoleID = organizationUserRole.RoleID
                WHERE menuItem.MenuItemCategoryID = @PlatformAdministratorCategoryID
                  AND menuItem.IsActive = 'Y'
                  AND organizationUser.OrganizationUserStatusID = @ActiveOrganizationUserStatusID
                  AND appUser.IsActive = 1
                  AND roleDefinition.IsActive = 'Y'
                  AND LOWER(
                      CASE
                          WHEN LEFT(LTRIM(RTRIM(menuItem.URL)), 1) = '/'
                              THEN LTRIM(RTRIM(menuItem.URL))
                          ELSE '/' + LTRIM(RTRIM(menuItem.URL))
                      END) = LOWER(@Route)
            ) THEN 1 ELSE 0 END;
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@PlatformOrganizationID", SqlDbType.Int).Value =
            _boundaryValidator.PlatformOrganizationID;
        command.Parameters.Add("@PlatformAdministratorCategoryID", SqlDbType.Int).Value =
            _boundaryValidator.PlatformAdministratorCategoryID;
        command.Parameters.Add("@UserID", SqlDbType.Int).Value = identity.UserID;
        command.Parameters.Add("@ActiveOrganizationUserStatusID", SqlDbType.Int).Value = ActiveOrganizationUserStatusID;
        command.Parameters.Add("@Route", SqlDbType.VarChar, 256).Value = route.Trim();

        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static bool IsOrganizationUser(ApiRequestIdentity identity) =>
        identity.ActorType == ApiActorType.OrganizationUser &&
        identity.EMAOrganizationID > 0 &&
        identity.UserID > 0;

    private static void AddOrganizationUserParameters(
        SqlCommand command,
        ApiRequestIdentity identity)
    {
        command.Parameters.Add("@OrganizationID", SqlDbType.Int).Value = identity.EMAOrganizationID;
        command.Parameters.Add("@UserID", SqlDbType.Int).Value = identity.UserID;
    }

    private async Task<SqlConnection> OpenConnectionAsync(
        CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
