/*
    Restrict EMA Insider roles to the platform organization
    -------------------------------------------------------
    RoleGroupID 90 classifies EMA Insider roles. RoleID 9000 is the
    current "Insider with Everything" role.

    Only EMAOrganizationID 1 may hold those roles. The explicit RoleID
    check preserves the current security boundary even if RoleGroupRole
    is accidentally changed, while the RoleGroup check automatically
    protects future Insider roles.

    Existing invalid assignments are reported and block deployment.
    They are never deleted silently.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'dbo.OrganizationUserRole', N'U') IS NULL
    THROW 51320,
        'dbo.OrganizationUserRole must exist before Insider-role restrictions can be installed.',
        1;
GO

IF OBJECT_ID(N'dbo.RoleGroupRole', N'U') IS NULL
    THROW 51321,
        'dbo.RoleGroupRole must exist before Insider-role restrictions can be installed.',
        1;
GO

IF EXISTS
(
    SELECT 1
    FROM dbo.OrganizationUserRole organizationUserRole
    WHERE organizationUserRole.EMAOrganizationID <> 1
      AND
      (
          organizationUserRole.RoleID = 9000
          OR EXISTS
          (
              SELECT 1
              FROM dbo.RoleGroupRole roleGroupRole
              WHERE roleGroupRole.RoleID =
                    organizationUserRole.RoleID
                AND roleGroupRole.RoleGroupID = 90
          )
      )
)
BEGIN
    SELECT
        organizationUserRole.EMAOrganizationID,
        organizationUserRole.UserID,
        organizationUserRole.RoleID
    FROM dbo.OrganizationUserRole organizationUserRole
    WHERE organizationUserRole.EMAOrganizationID <> 1
      AND
      (
          organizationUserRole.RoleID = 9000
          OR EXISTS
          (
              SELECT 1
              FROM dbo.RoleGroupRole roleGroupRole
              WHERE roleGroupRole.RoleID =
                    organizationUserRole.RoleID
                AND roleGroupRole.RoleGroupID = 90
          )
      )
    ORDER BY
        organizationUserRole.EMAOrganizationID,
        organizationUserRole.UserID,
        organizationUserRole.RoleID;

    THROW 51322,
        'A non-platform organization has an EMA Insider role. Remove or reclassify the listed assignments before rerunning this migration.',
        1;
END;
GO

CREATE OR ALTER TRIGGER
    dbo.TR_OrganizationUserRole_RestrictInsiderRoles
ON dbo.OrganizationUserRole
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT 1
        FROM inserted newAssignment
        WHERE newAssignment.EMAOrganizationID <> 1
          AND
          (
              newAssignment.RoleID = 9000
              OR EXISTS
              (
                  SELECT 1
                  FROM dbo.RoleGroupRole roleGroupRole
                  WHERE roleGroupRole.RoleID =
                        newAssignment.RoleID
                    AND roleGroupRole.RoleGroupID = 90
              )
          )
    )
    BEGIN
        THROW 51323,
            'EMA Insider roles can only be assigned within EMAOrganizationID 1.',
            1;
    END;
END;
GO

PRINT 'EMA Insider roles are restricted to EMAOrganizationID 1.';
GO
