/*
    Migration: 20260910_AddReferralCoverPageMenuItem.sql
    Description: Consolidate referral navigation to a single Menu link (EMA's Referral Program)
                 leading to the Cover Page (/referral-program), positioned right below Mailing Lists (22300)
                 with MenuItemID 22305, and configure existing sub-pages (/send-referrals, /accept-referral,
                 /referral-details) as Wizard/sub-screens ('W').

    Security Note: In accordance with PR #295, referral access is granular.
                   Seed ONLY the designated administrator roles:
                     - 2000 (Office Manager - EMA Donate)
                     - 2500 (Office Manager - EMA eTran)
                   Other roles must be granted referral access deliberately through role administration.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- 1. Collision check: MenuItemID 22305 must either not exist or already map to /referral-program
IF EXISTS (
    SELECT 1
    FROM dbo.MenuItem
    WHERE MenuItemID = 22305
      AND (URL IS NULL OR URL <> N'/referral-program')
)
BEGIN
    THROW 51000, N'MenuItemID 22305 already exists for a different menu item. Deployment halted.', 1;
END;

-- 2. Legacy MenuItem 19790 may only be the cover page created by an earlier version of this migration.
--    Fail before changing anything if another screen has claimed this ID.
IF EXISTS (
    SELECT 1
    FROM dbo.MenuItem
    WHERE MenuItemID = 19790
      AND (
          URL IS NULL OR URL <> N'/referral-program'
          OR MenuName IS NULL OR MenuName <> N'EMA''s Referral Program'
          OR ScreenTitle IS NULL OR ScreenTitle <> N'EMA''s Referral Program'
      )
)
BEGIN
    THROW 51001, N'MenuItemID 19790 exists but is not the expected legacy Referral Program menu item. Deployment halted.', 1;
END;

-- 3. Collision check: URL /referral-program must not be assigned to any other MenuItemID.
--    The validated legacy 19790 row is removed inside the transaction below.
IF EXISTS (
    SELECT 1
    FROM dbo.MenuItem
    WHERE URL = N'/referral-program'
      AND MenuItemID NOT IN (19790, 22305)
)
BEGIN
    THROW 51002, N'A menu item with URL /referral-program already exists with a different MenuItemID. Deployment halted.', 1;
END;

BEGIN TRY
    BEGIN TRANSACTION;

    -- 4. Remove the validated legacy Referral Program menu item and its role assignments.
    IF EXISTS (SELECT 1 FROM dbo.MenuItem WHERE MenuItemID = 19790)
    BEGIN
        DELETE FROM dbo.RoleMenuItem WHERE MenuItemID = 19790;
        DELETE FROM dbo.MenuItem WHERE MenuItemID = 19790;
    END;

    -- 5. Upsert MenuItem 22305 for the Cover Page
    IF NOT EXISTS (SELECT 1 FROM dbo.MenuItem WHERE MenuItemID = 22305)
    BEGIN
        INSERT INTO dbo.MenuItem
        (
            MenuItemCategoryID,
            MenuItemRowID,
            MenuItemID,
            MenuItemTypeID,
            MenuName,
            ScreenTitle,
            URL,
            DisplayControlID,
            IsActive
        )
        VALUES
        (
            20,
            2,
            22305,
            11,
            N'EMA''s Referral Program',
            N'EMA''s Referral Program',
            N'/referral-program',
            N'M',
            N'Y'
        );
    END
    ELSE
    BEGIN
        UPDATE dbo.MenuItem
        SET
            MenuItemCategoryID = 20,
            MenuItemRowID = 2,
            MenuItemTypeID = 11,
            MenuName = N'EMA''s Referral Program',
            ScreenTitle = N'EMA''s Referral Program',
            URL = N'/referral-program',
            DisplayControlID = N'M',
            IsActive = N'Y'
        WHERE MenuItemID = 22305;
    END;

    -- 6. Update existing referral sub-pages to DisplayControlID = 'W' (wizard / non-menu screens)
    UPDATE dbo.MenuItem
    SET DisplayControlID = N'W',
        IsActive = N'Y'
    WHERE URL IN (N'/send-referrals', N'/accept-referral', N'/referral-details');

    -- 7. Seed ONLY the established organization administrator roles (2000, 2500) per PR #295
    INSERT INTO dbo.RoleMenuItem (RoleID, MenuItemID)
    SELECT r.RoleID, 22305
    FROM dbo.[Role] r
    WHERE r.RoleID IN (2000, 2500)
      AND ISNULL(r.IsActive, N'Y') = N'Y'
      AND NOT EXISTS (
          SELECT 1 
          FROM dbo.RoleMenuItem rmi 
          WHERE rmi.RoleID = r.RoleID AND rmi.MenuItemID = 22305
      );

    COMMIT TRANSACTION;
    PRINT 'Successfully deployed Referral Program cover page menu configuration with granular role permissions.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
