using System.Data;
using EMA.Api.DTOs;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace EMA.Api.Services.Security;

public sealed class SqlApiSessionService :
    IApiSessionService
{
    private readonly string _connectionString;

    private readonly IApiAccessTokenService
        _accessTokenService;

    private readonly TimeSpan _sessionLifetime;

    public SqlApiSessionService(
        IConfiguration configuration,
        IApiAccessTokenService accessTokenService)
    {
        _accessTokenService =
            accessTokenService;

        _connectionString =
            configuration[
                "SqlConnectionString"]
            ?? Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString.");

        _sessionLifetime =
            TimeSpan.FromMinutes(
                ReadLifetimeMinutes(
                    configuration));
    }

    public async Task<ApiAccessToken>
        CreateSessionAsync(
            LoginResponseDto loginResponse,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            loginResponse);

        ValidateLoginResponse(
            loginResponse);

        var issuedAtUtc =
            DateTimeOffset.UtcNow;

        var identity =
            new ApiRequestIdentity(
                Guid.NewGuid(),
                Guid.NewGuid(),
                loginResponse.IsDonorSession
                    ? ApiActorType.Donor
                    : ApiActorType.OrganizationUser,
                loginResponse.EMAOrganizationID,
                loginResponse.UserID,
                loginResponse.DonorID,
                loginResponse.Roles
                    .Select(role => role.RoleID)
                    .Distinct()
                    .OrderBy(value => value)
                    .ToArray(),
                loginResponse.AllowedCategoryIDs
                    .Distinct()
                    .OrderBy(value => value)
                    .ToArray(),
                issuedAtUtc,
                issuedAtUtc.Add(
                    _sessionLifetime));

        await using var connection =
            new SqlConnection(
                _connectionString);

        await connection.OpenAsync(
            cancellationToken);

        await using var transaction =
            await connection
                .BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);

        const string revokeSql = """
            UPDATE dbo.ApiSession
            SET RevokedAtUtc = SYSUTCDATETIME()
            WHERE EMAOrganizationID = @EMAOrganizationID
              AND ActorType = @ActorType
              AND (
                    @ActorType = 'OrganizationUser'
                    AND UserID = @UserID
                    OR
                    @ActorType = 'Donor'
                    AND DonorID = @DonorID
                  )
              AND RevokedAtUtc IS NULL
              AND ExpiresAtUtc > SYSUTCDATETIME();
            """;

        await using (var revokeCommand =
                     new SqlCommand(
                         revokeSql,
                         connection,
                         (SqlTransaction)transaction))
        {
            AddIdentityParameters(
                revokeCommand,
                identity);

            await revokeCommand
                .ExecuteNonQueryAsync(
                    cancellationToken);
        }

        const string insertSql = """
            INSERT INTO dbo.ApiSession
            (
                ApiSessionID,
                SecurityStamp,
                ActorType,
                EMAOrganizationID,
                UserID,
                DonorID,
                IssuedAtUtc,
                ExpiresAtUtc,
                RevokedAtUtc
            )
            VALUES
            (
                @ApiSessionID,
                @SecurityStamp,
                @ActorType,
                @EMAOrganizationID,
                @UserID,
                @DonorID,
                @IssuedAtUtc,
                @ExpiresAtUtc,
                NULL
            );
            """;

        await using (var insertCommand =
                     new SqlCommand(
                         insertSql,
                         connection,
                         (SqlTransaction)transaction))
        {
            AddIdentityParameters(
                insertCommand,
                identity);

            insertCommand.Parameters.Add(
                "@IssuedAtUtc",
                SqlDbType.DateTime2)
                .Value =
                    identity.IssuedAtUtc.UtcDateTime;

            insertCommand.Parameters.Add(
                "@ExpiresAtUtc",
                SqlDbType.DateTime2)
                .Value =
                    identity.ExpiresAtUtc.UtcDateTime;

            await insertCommand
                .ExecuteNonQueryAsync(
                    cancellationToken);
        }

        await transaction.CommitAsync(
            cancellationToken);

        return _accessTokenService
            .CreateToken(
                identity);
    }

    public async Task<bool> IsSessionActiveAsync(
        ApiRequestIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            identity);

        var sql =
            identity.ActorType ==
            ApiActorType.OrganizationUser
                ? """
                  SELECT TOP (1) 1
                  FROM dbo.ApiSession session
                  INNER JOIN dbo.[User] appUser
                      ON appUser.UserID =
                         session.UserID
                  INNER JOIN dbo.OrganizationUser organizationUser
                      ON organizationUser.UserID =
                         session.UserID
                     AND organizationUser.EMAOrganizationID =
                         session.EMAOrganizationID
                  INNER JOIN dbo.Organization organization
                      ON organization.EMAOrganizationID =
                         organizationUser.EMAOrganizationID
                  WHERE session.ApiSessionID = @ApiSessionID
                    AND session.SecurityStamp = @SecurityStamp
                    AND session.ActorType = @ActorType
                    AND session.EMAOrganizationID = @EMAOrganizationID
                    AND session.UserID = @UserID
                    AND session.DonorID IS NULL
                    AND session.RevokedAtUtc IS NULL
                    AND session.ExpiresAtUtc > SYSUTCDATETIME()
                    AND organizationUser.OrganizationUserStatusID = 10
                    AND appUser.IsActive = 1
                    AND
                    (
                        ISNULL(
                            organization.OrganizationStatusID,
                            -1) <> 0
                        OR
                        (
                            organization.OrganizationStatusID = 0
                            AND appUser.SandboxOrganizationID =
                                session.EMAOrganizationID
                        )
                    );
                  """
                : """
                  SELECT TOP (1) 1
                  FROM dbo.ApiSession session
                  INNER JOIN dbo.Donor donor
                      ON donor.EMADonorID =
                         session.DonorID
                     AND donor.EMAOrganizationID =
                         session.EMAOrganizationID
                  WHERE session.ApiSessionID = @ApiSessionID
                    AND session.SecurityStamp = @SecurityStamp
                    AND session.ActorType = @ActorType
                    AND session.EMAOrganizationID = @EMAOrganizationID
                    AND session.UserID = @UserID
                    AND session.DonorID = @DonorID
                    AND session.RevokedAtUtc IS NULL
                    AND session.ExpiresAtUtc > SYSUTCDATETIME();
                  """;

        await using var connection =
            new SqlConnection(
                _connectionString);

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            new SqlCommand(
                sql,
                connection);

        AddIdentityParameters(
            command,
            identity);

        var result =
            await command.ExecuteScalarAsync(
                cancellationToken);

        if (result is null ||
            result == DBNull.Value)
        {
            return false;
        }

        return identity.ActorType ==
               ApiActorType.OrganizationUser
            ? await HasCurrentOrganizationUserAccessAsync(
                connection,
                identity,
                cancellationToken)
            : await HasCurrentDonorAccessAsync(
                connection,
                identity,
                cancellationToken);
    }

    public async Task RevokeSessionAsync(
        ApiRequestIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            identity);

        const string sql = """
            UPDATE dbo.ApiSession
            SET RevokedAtUtc =
                COALESCE(
                    RevokedAtUtc,
                    SYSUTCDATETIME())
            WHERE ApiSessionID = @ApiSessionID
              AND SecurityStamp = @SecurityStamp
              AND EMAOrganizationID = @EMAOrganizationID
              AND ActorType = @ActorType;
            """;

        await using var connection =
            new SqlConnection(
                _connectionString);

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            new SqlCommand(
                sql,
                connection);

        AddIdentityParameters(
            command,
            identity);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static void AddIdentityParameters(
        SqlCommand command,
        ApiRequestIdentity identity)
    {
        command.Parameters.Add(
            "@ApiSessionID",
            SqlDbType.UniqueIdentifier)
            .Value =
                identity.SessionID;

        command.Parameters.Add(
            "@SecurityStamp",
            SqlDbType.UniqueIdentifier)
            .Value =
                identity.SecurityStamp;

        command.Parameters.Add(
            "@ActorType",
            SqlDbType.VarChar,
            32)
            .Value =
                identity.ActorType.ToString();

        command.Parameters.Add(
            "@EMAOrganizationID",
            SqlDbType.Int)
            .Value =
                identity.EMAOrganizationID;

        command.Parameters.Add(
            "@UserID",
            SqlDbType.Int)
            .Value =
                identity.UserID;

        command.Parameters.Add(
            "@DonorID",
            SqlDbType.Int)
            .Value =
                identity.DonorID.HasValue
                    ? identity.DonorID.Value
                    : DBNull.Value;
    }

    private static void ValidateLoginResponse(
        LoginResponseDto loginResponse)
    {
        if (loginResponse.UserID <= 0 ||
            loginResponse.EMAOrganizationID <= 0 ||
            loginResponse.Roles.Count == 0 ||
            loginResponse.AllowedCategoryIDs.Count == 0 ||
            loginResponse.IsDonorSession &&
            (!loginResponse.DonorID.HasValue ||
             loginResponse.DonorID.Value <= 0) ||
            !loginResponse.IsDonorSession &&
            loginResponse.DonorID.HasValue)
        {
            throw new ArgumentException(
                "The authenticated login response is incomplete.",
                nameof(loginResponse));
        }
    }

    private static int ReadLifetimeMinutes(
        IConfiguration configuration)
    {
        const int defaultMinutes = 60;
        const int maximumMinutes = 480;

        return int.TryParse(
                   configuration[
                       "API_ACCESS_TOKEN_LIFETIME_MINUTES"],
                   out var configuredMinutes) &&
               configuredMinutes >= 5 &&
               configuredMinutes <=
                   maximumMinutes
            ? configuredMinutes
            : defaultMinutes;
    }

    private static async Task<bool>
        HasCurrentOrganizationUserAccessAsync(
            SqlConnection connection,
            ApiRequestIdentity identity,
            CancellationToken cancellationToken)
    {
        const string roleSql = """
            SELECT DISTINCT
                role.RoleID
            FROM dbo.OrganizationUserRole
                organizationUserRole
            INNER JOIN dbo.[Role] role
                ON role.RoleID =
                   organizationUserRole.RoleID
            WHERE organizationUserRole
                      .EMAOrganizationID =
                  @EMAOrganizationID
              AND organizationUserRole.UserID =
                  @UserID
              AND ISNULL(
                      role.IsActive,
                      'Y') = 'Y';
            """;

        var currentRoleIDs =
            await ReadIntegerSetAsync(
                connection,
                roleSql,
                identity,
                cancellationToken);

        if (currentRoleIDs.Count == 0 ||
            !currentRoleIDs.SetEquals(
                identity.RoleIDs))
        {
            return false;
        }

        const string categorySql = """
            SELECT DISTINCT
                menuItem.MenuItemCategoryID
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
              AND menuItem.MenuItemCategoryID
                  IS NOT NULL
              AND ISNULL(
                      menuItem.IsActive,
                      'Y') = 'Y';
            """;

        var currentCategoryIDs =
            await ReadIntegerSetAsync(
                connection,
                categorySql,
                identity,
                cancellationToken);

        currentCategoryIDs.Add(
            10);

        return currentCategoryIDs.SetEquals(
            identity.AllowedCategoryIDs);
    }

    private static async Task<bool>
        HasCurrentDonorAccessAsync(
            SqlConnection connection,
            ApiRequestIdentity identity,
            CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                organization.DonorRoleID
            FROM dbo.Organization organization
            INNER JOIN dbo.[Role] role
                ON role.RoleID =
                   organization.DonorRoleID
            WHERE organization
                      .EMAOrganizationID =
                  @EMAOrganizationID
              AND organization.DonorRoleID
                  IS NOT NULL
              AND ISNULL(
                      organization.OrganizationStatusID,
                      -1) <> 0
              AND ISNULL(
                      role.IsActive,
                      'Y') = 'Y';
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection);

        AddIdentityParameters(
            command,
            identity);

        var result =
            await command.ExecuteScalarAsync(
                cancellationToken);

        if (result is null ||
            result == DBNull.Value)
        {
            return false;
        }

        var currentRoleID =
            Convert.ToInt32(
                result);

        return identity.RoleIDs
                   .Distinct()
                   .SequenceEqual(
                       new[]
                       {
                           currentRoleID
                       }) &&
               identity.AllowedCategoryIDs
                   .ToHashSet()
                   .SetEquals(
                       new[]
                       {
                           10,
                           30
                       });
    }

    private static async Task<HashSet<int>>
        ReadIntegerSetAsync(
            SqlConnection connection,
            string sql,
            ApiRequestIdentity identity,
            CancellationToken cancellationToken)
    {
        await using var command =
            new SqlCommand(
                sql,
                connection);

        AddIdentityParameters(
            command,
            identity);

        var values =
            new HashSet<int>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            if (reader.IsDBNull(
                    0))
            {
                continue;
            }

            values.Add(
                Convert.ToInt32(
                    reader.GetValue(
                        0)));
        }

        return values;
    }
}