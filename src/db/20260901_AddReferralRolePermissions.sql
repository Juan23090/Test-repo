/*
    Migration: Grant the Referral Program menu items to organization staff roles.

    Must run AFTER 20260901_AddOrganizationReferralMenuItems.sql, which owns the
    dbo.MenuItem rows. See InstallReferralProgramSchema.sql for the full run order.

    Scope note: sending referrals commits the organization to emailing third
    parties, and the referral history exposes the organization's invitation list
    and credit balance. Seed only the two established organization administrator
    roles (Office Manager - EMA Donate and Office Manager - EMA eTran).
    RoleMenuItem remains granular, so other roles can be granted these screens
    deliberately through normal role administration.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @ReferralMenuItemIDs TABLE (MenuItemID INT NOT NULL PRIMARY KEY);

    INSERT INTO @ReferralMenuItemIDs (MenuItemID)
    VALUES (19800), (19810), (19825);

    IF EXISTS (
        SELECT 1
        FROM @ReferralMenuItemIDs required
        WHERE NOT EXISTS (
            SELECT 1
            FROM dbo.MenuItem existing
            WHERE existing.MenuItemID = required.MenuItemID
        )
    )
        THROW 51400,
            'Run 20260901_AddOrganizationReferralMenuItems.sql before granting referral role permissions.',
            1;

    INSERT INTO dbo.RoleMenuItem (RoleID, MenuItemID)
    SELECT r.RoleID, m.MenuItemID
    FROM dbo.[Role] r
    CROSS JOIN @ReferralMenuItemIDs m
    WHERE r.RoleID IN (2000, 2500) -- Organization Administrator roles
      AND ISNULL(r.IsActive, N'Y') = N'Y'
      AND NOT EXISTS (
          SELECT 1
          FROM dbo.RoleMenuItem rmi
          WHERE rmi.RoleID = r.RoleID AND rmi.MenuItemID = m.MenuItemID
      );

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
