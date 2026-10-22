/*
    Remove legacy dbo.UserRoles
    ---------------------------
    EMA role assignments are organization-scoped and dbo.OrganizationUserRole
    is the authoritative user-to-role relationship.

    dbo.UserRoles is the older global UserID -> RoleID mapping. Keeping both
    tables creates two competing authorization sources and allows code to lose
    the organization boundary when resolving permissions.

    This migration is intentionally defensive. It will refuse to drop
    dbo.UserRoles when:
      - dbo.OrganizationUserRole is unavailable or does not have the expected
        organization-scoped key columns;
      - a legacy UserID/RoleID pair is not represented anywhere in
        dbo.OrganizationUserRole;
      - a persisted SQL module still references dbo.UserRoles/UserRoles; or
      - another table has a foreign key that references dbo.UserRoles.

    Application code must use:
        EMAOrganizationID + UserID + RoleID
    through dbo.OrganizationUserRole.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'dbo.UserRoles', N'U') IS NULL
BEGIN
    PRINT 'dbo.UserRoles does not exist. No legacy role table cleanup is required.';
    RETURN;
END;
GO

IF OBJECT_ID(N'dbo.OrganizationUserRole', N'U') IS NULL
    THROW 51300,
        'dbo.OrganizationUserRole must exist before dbo.UserRoles can be retired.',
        1;
GO

IF COL_LENGTH(N'dbo.OrganizationUserRole', N'EMAOrganizationID') IS NULL
   OR COL_LENGTH(N'dbo.OrganizationUserRole', N'UserID') IS NULL
   OR COL_LENGTH(N'dbo.OrganizationUserRole', N'RoleID') IS NULL
    THROW 51301,
        'dbo.OrganizationUserRole is missing one or more required columns: EMAOrganizationID, UserID, RoleID.',
        1;
GO

/*
    Do not silently discard a legacy user/role pair that has never been moved
    into the organization-scoped relationship. We cannot infer the intended
    organization for that legacy row safely, so deployment must stop instead.
*/
IF EXISTS
(
    SELECT 1
    FROM dbo.UserRoles legacyRole
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.OrganizationUserRole organizationRole
        WHERE organizationRole.UserID = legacyRole.UserID
          AND organizationRole.RoleID = legacyRole.RoleID
    )
)
BEGIN
    SELECT
        legacyRole.UserID,
        legacyRole.RoleID
    FROM dbo.UserRoles legacyRole
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.OrganizationUserRole organizationRole
        WHERE organizationRole.UserID = legacyRole.UserID
          AND organizationRole.RoleID = legacyRole.RoleID
    )
    ORDER BY
        legacyRole.UserID,
        legacyRole.RoleID;

    THROW 51302,
        'dbo.UserRoles contains assignments that are not represented in dbo.OrganizationUserRole. Resolve their organization explicitly before rerunning this migration.',
        1;
END;
GO

/*
    Persisted SQL objects must no longer read the global table. Dynamic SQL
    cannot be discovered perfectly, but this catches normal procedures, views,
    functions, and triggers stored in sys.sql_modules.
*/
IF EXISTS
(
    SELECT 1
    FROM sys.sql_modules moduleDefinition
    INNER JOIN sys.objects databaseObject
        ON databaseObject.object_id = moduleDefinition.object_id
    WHERE databaseObject.object_id <> OBJECT_ID(N'dbo.UserRoles')
      AND
      (
          moduleDefinition.definition LIKE N'%dbo.UserRoles%'
          OR moduleDefinition.definition LIKE N'%[[]dbo[]].[[]UserRoles[]]%'
          OR moduleDefinition.definition LIKE N'%FROM UserRoles%'
          OR moduleDefinition.definition LIKE N'%JOIN UserRoles%'
          OR moduleDefinition.definition LIKE N'%INTO UserRoles%'
          OR moduleDefinition.definition LIKE N'%UPDATE UserRoles%'
          OR moduleDefinition.definition LIKE N'%DELETE FROM UserRoles%'
      )
)
BEGIN
    SELECT
        SCHEMA_NAME(databaseObject.schema_id) AS SchemaName,
        databaseObject.name AS ObjectName,
        databaseObject.type_desc AS ObjectType
    FROM sys.sql_modules moduleDefinition
    INNER JOIN sys.objects databaseObject
        ON databaseObject.object_id = moduleDefinition.object_id
    WHERE databaseObject.object_id <> OBJECT_ID(N'dbo.UserRoles')
      AND
      (
          moduleDefinition.definition LIKE N'%dbo.UserRoles%'
          OR moduleDefinition.definition LIKE N'%[[]dbo[]].[[]UserRoles[]]%'
          OR moduleDefinition.definition LIKE N'%FROM UserRoles%'
          OR moduleDefinition.definition LIKE N'%JOIN UserRoles%'
          OR moduleDefinition.definition LIKE N'%INTO UserRoles%'
          OR moduleDefinition.definition LIKE N'%UPDATE UserRoles%'
          OR moduleDefinition.definition LIKE N'%DELETE FROM UserRoles%'
      )
    ORDER BY
        SchemaName,
        ObjectName;

    THROW 51303,
        'One or more persisted SQL modules still reference dbo.UserRoles. Migrate them to dbo.OrganizationUserRole before dropping the legacy table.',
        1;
END;
GO

/* Do not break an unexpected incoming relational dependency. */
IF EXISTS
(
    SELECT 1
    FROM sys.foreign_keys foreignKey
    WHERE foreignKey.referenced_object_id = OBJECT_ID(N'dbo.UserRoles')
      AND foreignKey.parent_object_id <> OBJECT_ID(N'dbo.UserRoles')
)
BEGIN
    SELECT
        foreignKey.name AS ForeignKeyName,
        OBJECT_SCHEMA_NAME(foreignKey.parent_object_id) AS ReferencingSchema,
        OBJECT_NAME(foreignKey.parent_object_id) AS ReferencingTable
    FROM sys.foreign_keys foreignKey
    WHERE foreignKey.referenced_object_id = OBJECT_ID(N'dbo.UserRoles')
      AND foreignKey.parent_object_id <> OBJECT_ID(N'dbo.UserRoles')
    ORDER BY
        ReferencingSchema,
        ReferencingTable,
        ForeignKeyName;

    THROW 51304,
        'Another table still has a foreign key to dbo.UserRoles. Remove or migrate that dependency before dropping the legacy table.',
        1;
END;
GO

BEGIN TRANSACTION;

BEGIN TRY
    DROP TABLE dbo.UserRoles;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
GO

IF OBJECT_ID(N'dbo.UserRoles', N'U') IS NOT NULL
    THROW 51305,
        'dbo.UserRoles still exists after the migration completed.',
        1;
GO

PRINT 'dbo.UserRoles retired successfully. dbo.OrganizationUserRole is the authoritative role-assignment table.';
GO
