/*
    Migration: Referral Program menu items on the Organization menu (Category 20)

    Category 20: Organization (Row 1 is the Organization parent header, Row 2 are dropdown items)
    - 19800: Send Referrals   (/send-referrals)
    - 19810: Accept Referral  (/accept-referral)
    - 19825: Referral Details (/referral-details)

    This file owns all three dbo.MenuItem rows. Role assignment lives in
    20260901_AddReferralRolePermissions.sql, which must run after this file.
    See InstallReferralProgramSchema.sql for the full run order.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @ReferralMenuItems TABLE (
        MenuItemID INT NOT NULL PRIMARY KEY,
        MenuName NVARCHAR(255) NOT NULL,
        ScreenTitle NVARCHAR(255) NOT NULL,
        URL NVARCHAR(255) NOT NULL,
        DisplayControlID NVARCHAR(10) NOT NULL
    );

    INSERT INTO @ReferralMenuItems (MenuItemID, MenuName, ScreenTitle, URL, DisplayControlID)
    VALUES
        (19800, N'Send Referrals',   N'Send Referral Invitations',  N'/send-referrals',   N'M'),
        (19810, N'Accept Referral',  N'Accept a Referral',          N'/accept-referral',  N'M'),
        (19825, N'Referral Details', N'Referral Program Summary',   N'/referral-details', N'M');

    -- 1. Collision check: ensure none of the designated IDs exist with a different URL
    IF EXISTS (
        SELECT 1
        FROM dbo.MenuItem existing
        INNER JOIN @ReferralMenuItems source
            ON existing.MenuItemID = source.MenuItemID
        WHERE existing.URL IS NULL
           OR existing.URL <> source.URL
    )
    BEGIN
        THROW 51410, N'A referral MenuItemID already exists with a different URL. Deployment halted.', 1;
    END;

    -- 2. Collision check: ensure none of the referral URLs exist under a different MenuItemID
    IF EXISTS (
        SELECT 1
        FROM dbo.MenuItem existing
        INNER JOIN @ReferralMenuItems source
            ON existing.URL = source.URL
        WHERE existing.MenuItemID <> source.MenuItemID
    )
    BEGIN
        THROW 51411, N'A referral URL already exists under a different MenuItemID. Deployment halted.', 1;
    END;

    UPDATE existing
    SET
        existing.MenuItemCategoryID = 20,   -- Organization
        existing.MenuItemRowID = 2,         -- Dropdown child item
        existing.MenuItemTypeID = 11,       -- Standard page
        existing.MenuName = source.MenuName,
        existing.ScreenTitle = source.ScreenTitle,
        existing.URL = source.URL,
        existing.DisplayControlID = source.DisplayControlID,
        existing.IsActive = N'Y'
    FROM dbo.MenuItem existing
    INNER JOIN @ReferralMenuItems source
        ON source.MenuItemID = existing.MenuItemID
       AND source.URL = existing.URL;

    INSERT INTO dbo.MenuItem (
        MenuItemID,
        MenuItemCategoryID,
        MenuItemRowID,
        MenuItemTypeID,
        MenuName,
        ScreenTitle,
        URL,
        DisplayControlID,
        IsActive
    )
    SELECT
        source.MenuItemID,
        20,
        2,
        11,
        source.MenuName,
        source.ScreenTitle,
        source.URL,
        source.DisplayControlID,
        N'Y'
    FROM @ReferralMenuItems source
    WHERE NOT EXISTS (
        SELECT 1
        FROM dbo.MenuItem existing
        WHERE existing.MenuItemID = source.MenuItemID
    );

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
