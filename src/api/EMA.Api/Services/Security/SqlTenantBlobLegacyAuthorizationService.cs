using System.Data;
using EMA.Api.Services.Navigation;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace EMA.Api.Services.Security;

public sealed class
    SqlTenantBlobLegacyAuthorizationService :
        ITenantBlobLegacyAuthorizationService
{
    private const int BrochureCategoryID = 10;
    private const int DonorsCategoryID = 30;

    private readonly string _connectionString;
    private readonly ApiRequestBoundaryValidator _authorization;

    public SqlTenantBlobLegacyAuthorizationService(
        IConfiguration configuration,
        ApiRequestBoundaryValidator authorization)
    {
        _connectionString =
            configuration["SqlConnectionString"]
            ?? Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString.");

        _authorization = authorization;
    }

    public async Task<TenantBlobReadAuthorization>
        AuthorizeReadAsync(
            ApiRequestIdentity identity,
            string blobID,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);

        if (identity.EMAOrganizationID <= 0 ||
            string.IsNullOrWhiteSpace(blobID))
        {
            return TenantBlobReadAuthorization.Denied;
        }

        var normalizedBlobID = blobID.Trim();

        await using var connection =
            new SqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        var organizationReference =
            await GetOrganizationReferenceKindAsync(
                connection,
                identity.EMAOrganizationID,
                normalizedBlobID,
                cancellationToken);

        // Sensitive organization-level records take precedence over media.
        // This prevents a donor from gaining ETL access if an ID is ever reused.
        if (organizationReference ==
            TenantBlobOrganizationReferenceKind.SensitiveOrganizationData)
        {
            return TenantBlobReadAccessPolicy.Evaluate(
                identity.ActorType,
                organizationReference,
                canAccessMenuItemMedia: false);
        }

        var canAccessMenuItemMedia = false;

        if (await MenuItemResourceQuery.TablesExistAsync(
                connection,
                cancellationToken: cancellationToken))
        {
            canAccessMenuItemMedia =
                identity.ActorType == ApiActorType.OrganizationUser
                    ? await OrganizationUserCanAccessMenuItemMediaAsync(
                        connection,
                        identity,
                        normalizedBlobID,
                        _authorization.IsOrganizationAdministrator(identity),
                        cancellationToken)
                    : await DonorCanAccessMenuItemMediaAsync(
                        connection,
                        identity,
                        normalizedBlobID,
                        cancellationToken);
        }

        return TenantBlobReadAccessPolicy.Evaluate(
            identity.ActorType,
            organizationReference,
            canAccessMenuItemMedia);
    }

    public async Task<bool>
        IsReferencedByOrganizationAsync(
            int EMAOrganizationID,
            string blobID,
            CancellationToken cancellationToken = default)
    {
        if (EMAOrganizationID <= 0 ||
            string.IsNullOrWhiteSpace(blobID))
        {
            return false;
        }

        await using var connection =
            new SqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        return await GetOrganizationReferenceKindAsync(
                   connection,
                   EMAOrganizationID,
                   blobID.Trim(),
                   cancellationToken) !=
               TenantBlobOrganizationReferenceKind.None;
    }

    private static async Task<TenantBlobOrganizationReferenceKind>
        GetOrganizationReferenceKindAsync(
            SqlConnection connection,
            int organizationID,
            string blobID,
            CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP (1)
                CASE
                    WHEN LogoBlobID = @BlobID THEN 1
                    WHEN ETLDonorDataBlobID = @BlobID
                      OR ETLDonationDataBlobID = @BlobID THEN 2
                    ELSE 0
                END
            FROM dbo.Organization
            WHERE EMAOrganizationID = @EMAOrganizationID
              AND
              (
                  LogoBlobID = @BlobID
                  OR ETLDonorDataBlobID = @BlobID
                  OR ETLDonationDataBlobID = @BlobID
              );
            """;

        await using var command =
            new SqlCommand(sql, connection);

        command.Parameters.Add(
            "@EMAOrganizationID",
            SqlDbType.Int)
            .Value = organizationID;

        command.Parameters.Add(
            "@BlobID",
            SqlDbType.VarChar,
            36)
            .Value = blobID;

        var result =
            await command.ExecuteScalarAsync(
                cancellationToken);

        if (result is null || result == DBNull.Value)
        {
            return TenantBlobOrganizationReferenceKind.None;
        }

        return (TenantBlobOrganizationReferenceKind)
            Convert.ToInt32(result);
    }

    private static async Task<bool>
        OrganizationUserCanAccessMenuItemMediaAsync(
            SqlConnection connection,
            ApiRequestIdentity identity,
            string blobID,
            bool isAdministrator,
            CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT CASE
                WHEN EXISTS
                (
                    SELECT 1
                    FROM dbo.MenuItemResource mir
                    INNER JOIN dbo.MenuItem mi
                        ON mi.MenuItemID = mir.MenuItemID
                    LEFT JOIN dbo.MenuItemCategory mic
                        ON mic.MenuItemCategoryID = mi.MenuItemCategoryID
                    WHERE mir.AudioVideoBlob = @BlobID
                      AND UPPER(mir.MenuItemResourceTypeID) IN ('A', 'V')
                      AND mi.IsActive = 'Y'
                      AND mi.MenuItemCategoryID IS NOT NULL
                      AND EXISTS
                      (
                          SELECT 1
                          FROM dbo.OrganizationUser ou
                          INNER JOIN dbo.[User] appUser
                              ON appUser.UserID = ou.UserID
                          WHERE ou.EMAOrganizationID = @EMAOrganizationID
                            AND ou.UserID = @UserID
                            AND appUser.IsActive = 1
                      )
                      AND
                      (
                          mi.MenuItemCategoryID = @BrochureCategoryID
                          OR
                          (
                              EXISTS
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
                                  WHERE categoryMenuItem.MenuItemCategoryID = mi.MenuItemCategoryID
                                    AND categoryMenuItem.IsActive = 'Y'
                                    AND categoryUserRole.UserID = @UserID
                                    AND categoryAppUser.IsActive = 1
                                    AND categoryRole.IsActive = 'Y'
                              )
                              AND
                              (
                                  @IsAdministrator = 1
                                  OR mic.LandingPageMenuItemID = mi.MenuItemID
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
                                      WHERE roleMenuItem.MenuItemID = mi.MenuItemID
                                        AND userRole.UserID = @UserID
                                        AND appUser.IsActive = 1
                                        AND roleDefinition.IsActive = 'Y'
                                  )
                              )
                          )
                      )
                )
                THEN 1
                ELSE 0
            END;
            """;

        await using var command =
            new SqlCommand(sql, connection);

        command.Parameters.Add(
            "@BlobID",
            SqlDbType.VarChar,
            36)
            .Value = blobID;

        command.Parameters.Add(
            "@EMAOrganizationID",
            SqlDbType.Int)
            .Value = identity.EMAOrganizationID;

        command.Parameters.Add(
            "@UserID",
            SqlDbType.Int)
            .Value = identity.UserID;

        command.Parameters.Add(
            "@IsAdministrator",
            SqlDbType.Bit)
            .Value = isAdministrator;

        command.Parameters.Add(
            "@BrochureCategoryID",
            SqlDbType.Int)
            .Value = BrochureCategoryID;

        return Convert.ToInt32(
                   await command.ExecuteScalarAsync(
                       cancellationToken)) == 1;
    }

    private static async Task<bool>
        DonorCanAccessMenuItemMediaAsync(
            SqlConnection connection,
            ApiRequestIdentity identity,
            string blobID,
            CancellationToken cancellationToken)
    {
        if (!identity.DonorID.HasValue)
        {
            return false;
        }

        const string sql = """
            SELECT CASE
                WHEN EXISTS
                (
                    SELECT 1
                    FROM dbo.MenuItemResource mir
                    INNER JOIN dbo.MenuItem mi
                        ON mi.MenuItemID = mir.MenuItemID
                    LEFT JOIN dbo.MenuItemCategory mic
                        ON mic.MenuItemCategoryID = mi.MenuItemCategoryID
                    WHERE mir.AudioVideoBlob = @BlobID
                      AND UPPER(mir.MenuItemResourceTypeID) IN ('A', 'V')
                      AND mi.IsActive = 'Y'
                      AND mi.MenuItemCategoryID IS NOT NULL
                      AND EXISTS
                      (
                          SELECT 1
                          FROM dbo.Donor donor
                          WHERE donor.EMADonorID = @DonorID
                            AND donor.EMAOrganizationID = @EMAOrganizationID
                      )
                      AND
                      (
                          mi.MenuItemCategoryID = @BrochureCategoryID
                          OR
                          (
                              mi.MenuItemCategoryID = @DonorsCategoryID
                              AND EXISTS
                              (
                                  SELECT 1
                                  FROM dbo.Organization organization
                                  INNER JOIN dbo.[Role] donorRole
                                      ON donorRole.RoleID = organization.DonorRoleID
                                  INNER JOIN dbo.RoleMenuItem categoryRoleMenuItem
                                      ON categoryRoleMenuItem.RoleID = donorRole.RoleID
                                  INNER JOIN dbo.MenuItem categoryMenuItem
                                      ON categoryMenuItem.MenuItemID = categoryRoleMenuItem.MenuItemID
                                  WHERE organization.EMAOrganizationID = @EMAOrganizationID
                                    AND categoryMenuItem.MenuItemCategoryID = mi.MenuItemCategoryID
                                    AND categoryMenuItem.IsActive = 'Y'
                                    AND donorRole.IsActive = 'Y'
                              )
                              AND
                              (
                                  mic.LandingPageMenuItemID = mi.MenuItemID
                                  OR EXISTS
                                  (
                                      SELECT 1
                                      FROM dbo.Organization organization
                                      INNER JOIN dbo.[Role] donorRole
                                          ON donorRole.RoleID = organization.DonorRoleID
                                      INNER JOIN dbo.RoleMenuItem roleMenuItem
                                          ON roleMenuItem.RoleID = donorRole.RoleID
                                      WHERE organization.EMAOrganizationID = @EMAOrganizationID
                                        AND roleMenuItem.MenuItemID = mi.MenuItemID
                                        AND donorRole.IsActive = 'Y'
                                  )
                              )
                          )
                      )
                )
                THEN 1
                ELSE 0
            END;
            """;

        await using var command =
            new SqlCommand(sql, connection);

        command.Parameters.Add(
            "@BlobID",
            SqlDbType.VarChar,
            36)
            .Value = blobID;

        command.Parameters.Add(
            "@EMAOrganizationID",
            SqlDbType.Int)
            .Value = identity.EMAOrganizationID;

        command.Parameters.Add(
            "@DonorID",
            SqlDbType.Int)
            .Value = identity.DonorID.Value;

        command.Parameters.Add(
            "@BrochureCategoryID",
            SqlDbType.Int)
            .Value = BrochureCategoryID;

        command.Parameters.Add(
            "@DonorsCategoryID",
            SqlDbType.Int)
            .Value = DonorsCategoryID;

        return Convert.ToInt32(
                   await command.ExecuteScalarAsync(
                       cancellationToken)) == 1;
    }
}
