/*
    Task: Add Public Brochure Menu Item for EMA's Referral Program
    Route: /referrals-public
    Screen Title: EMA’s Referral Program
    Menu Name: Our Referral Program
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF EXISTS (
    SELECT 1
    FROM dbo.MenuItem
    WHERE MenuItemID = 10900
      AND URL <> N'/referrals-public'
)
BEGIN
    THROW 51000, N'MenuItemID 10900 already exists for a different menu item. Deployment halted.', 1;
END;

IF EXISTS (
    SELECT 1
    FROM dbo.MenuItem
    WHERE URL = N'/referrals-public'
      AND MenuItemID <> 10900
)
BEGIN
    THROW 51001, N'A menu item with URL /referrals-public already exists with a different MenuItemID. Deployment halted.', 1;
END;

BEGIN TRANSACTION;

IF NOT EXISTS (SELECT 1 FROM dbo.MenuItem WHERE MenuItemID = 10900)
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
        10,
        2,
        10900,
        11,
        N'Our Referral Program',
        N'EMA’s Referral Program',
        N'/referrals-public',
        N'M',
        N'Y'
    );
END
ELSE
BEGIN
    UPDATE dbo.MenuItem
    SET
        MenuItemCategoryID = 10,
        MenuItemRowID = 2,
        MenuItemTypeID = 11,
        MenuName = N'Our Referral Program',
        ScreenTitle = N'EMA’s Referral Program',
        URL = N'/referrals-public',
        DisplayControlID = N'M',
        IsActive = N'Y'
    WHERE MenuItemID = 10900;
END;

-- Remove any unintended RoleMenuItem assignments for this public brochure menu item
DELETE FROM dbo.RoleMenuItem
WHERE MenuItemID = 10900;

COMMIT TRANSACTION;
GO
