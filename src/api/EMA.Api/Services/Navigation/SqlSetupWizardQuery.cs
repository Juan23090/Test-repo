using EMA.Api.DTOs;
using System.Data;
using Microsoft.Data.SqlClient;

namespace EMA.Api.Services.Navigation;

public sealed class SqlSetupWizardQuery : ISetupWizardQuery
{
    private const int OrganizationCategoryID = 20;

    private readonly string _connectionString;

    public SqlSetupWizardQuery()
    {
        _connectionString =
            Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString.");
    }

    public Task<SetupWizardResponseDto?> GetForOrganizationUserAsync(
        int organizationID,
        int userID,
        bool includeAllWizardSteps,
        CancellationToken cancellationToken = default)
    {
        return LoadAsync(
            organizationID,
            userID,
            includeAllWizardSteps,
            cancellationToken);
    }

    public Task<SetupWizardResponseDto?> GetForPlatformAdministratorAsync(
        int organizationID,
        CancellationToken cancellationToken = default)
    {
        return LoadAsync(
            organizationID,
            userID: null,
            includeAllWizardSteps: true,
            cancellationToken: cancellationToken);
    }

    private async Task<SetupWizardResponseDto?> LoadAsync(
        int organizationID,
        int? userID,
        bool includeAllWizardSteps,
        CancellationToken cancellationToken)
    {
        if (organizationID <= 0)
        {
            return null;
        }

        await using var connection =
            new SqlConnection(
                _connectionString);

        await connection.OpenAsync(
            cancellationToken);

        var organizationName =
            await GetOrganizationNameAsync(
                connection,
                organizationID,
                cancellationToken);

        if (organizationName is null)
        {
            return null;
        }

        const string sql = """
            WITH UsageByOrganization AS
            (
                SELECT
                    usage.MenuItemID,
                    COUNT_BIG(1) AS VisitCount,
                    MAX(usage.DateTimeLogged) AS LastVisitUtc
                FROM dbo.MenuItemUsage AS usage
                LEFT JOIN dbo.ApiSession AS apiSession
                    ON usage.ApiSessionID = apiSession.ApiSessionID
                LEFT JOIN dbo.AuthenticationSession AS authSession
                    ON usage.AuthenticationSessionID = authSession.AuthenticationSessionID
                LEFT JOIN dbo.LoginInstance AS loginInstance
                    ON usage.LoginInstanceID = loginInstance.LoginInstanceID
                LEFT JOIN dbo.OrganizationUser AS loginOrganizationUser
                    ON loginInstance.OrganizationUserID = loginOrganizationUser.OrganizationUserID
                WHERE COALESCE(
                          apiSession.EMAOrganizationID,
                          authSession.EMAOrganizationID,
                          loginOrganizationUser.EMAOrganizationID
                      ) = @OrganizationID
                GROUP BY usage.MenuItemID
            )
            SELECT
                mi.MenuItemID,
                mi.MenuName,
                mi.ScreenTitle,
                mi.URL,
                COALESCE(usage.VisitCount, 0) AS VisitCount,
                usage.LastVisitUtc
            FROM dbo.MenuItem AS mi
            LEFT JOIN UsageByOrganization AS usage
                ON usage.MenuItemID = mi.MenuItemID
            WHERE mi.MenuItemCategoryID = @OrganizationCategoryID
              AND mi.IsActive = 'Y'
              AND UPPER(
                      COALESCE(
                          NULLIF(LTRIM(RTRIM(mi.DisplayControlID)), ''),
                          'M'
                      )
                  ) IN ('W', 'B')
              AND NULLIF(LTRIM(RTRIM(mi.URL)), '') IS NOT NULL
              AND
              (
                  @IncludeAllWizardSteps = 1
                  OR EXISTS
                  (
                      SELECT 1
                      FROM dbo.RoleMenuItem AS roleMenuItem
                      INNER JOIN dbo.OrganizationUserRole AS organizationUserRole
                          ON roleMenuItem.RoleID = organizationUserRole.RoleID
                         AND organizationUserRole.EMAOrganizationID = @OrganizationID
                      INNER JOIN dbo.OrganizationUser AS organizationUser
                          ON organizationUserRole.UserID = organizationUser.UserID
                         AND organizationUser.EMAOrganizationID = @OrganizationID
                      INNER JOIN dbo.[User] AS appUser
                          ON organizationUserRole.UserID = appUser.UserID
                      INNER JOIN dbo.[Role] AS appRole
                          ON organizationUserRole.RoleID = appRole.RoleID
                      WHERE roleMenuItem.MenuItemID = mi.MenuItemID
                        AND organizationUserRole.UserID = @UserID
                        AND appUser.IsActive = 1
                        AND appRole.IsActive = 'Y'
                  )
              )
            ORDER BY
                mi.MenuItemRowID,
                mi.MenuItemID;
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters.Add(
            "@OrganizationID",
            SqlDbType.Int)
            .Value = organizationID;

        command.Parameters.Add(
            "@OrganizationCategoryID",
            SqlDbType.Int)
            .Value = OrganizationCategoryID;

        command.Parameters.Add(
            "@IncludeAllWizardSteps",
            SqlDbType.Bit)
            .Value = includeAllWizardSteps;

        var userParameter =
            command.Parameters.Add(
                "@UserID",
                SqlDbType.Int);

        userParameter.Value =
            userID.HasValue
                ? userID.Value
                : DBNull.Value;

        var steps =
            new List<SetupWizardStepDto>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            steps.Add(
                new SetupWizardStepDto
                {
                    MenuItemID =
                        Convert.ToInt32(
                            reader["MenuItemID"]),

                    MenuName =
                        reader["MenuName"]?.ToString()
                        ?? string.Empty,

                    ScreenTitle =
                        reader["ScreenTitle"]?.ToString()
                        ?? string.Empty,

                    Url =
                        reader["URL"]?.ToString()?.Trim()
                        ?? string.Empty,

                    VisitCount =
                        Convert.ToInt64(
                            reader["VisitCount"]),

                    LastVisitUtc =
                        reader["LastVisitUtc"] is DBNull
                            ? null
                            : Convert.ToDateTime(
                                reader["LastVisitUtc"])
                });
        }

        return new SetupWizardResponseDto
        {
            EMAOrganizationID = organizationID,
            OrganizationName = organizationName,
            Steps = steps
        };
    }

    private static async Task<string?> GetOrganizationNameAsync(
        SqlConnection connection,
        int organizationID,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT OrganizationName
            FROM dbo.Organization
            WHERE EMAOrganizationID = @OrganizationID;
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters.Add(
            "@OrganizationID",
            SqlDbType.Int)
            .Value = organizationID;

        var value =
            await command.ExecuteScalarAsync(
                cancellationToken);

        return value is null ||
               value == DBNull.Value
            ? null
            : value.ToString()?.Trim();
    }
}
