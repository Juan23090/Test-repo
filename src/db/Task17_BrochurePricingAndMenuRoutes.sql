USE [emasqldatabase-dev];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'dbo.MenuItem', N'U') IS NULL
BEGIN
    THROW 51000, 'dbo.MenuItem does not exist.', 1;
END;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    /* Convert Compare from an external Google Slides link to the new EMA screen. */
    UPDATE dbo.MenuItem
    SET MenuItemTypeID = 11,
        MenuName = N'Compare',
        ScreenTitle = N'EMA vs. Others',
        URL = N'/compare-ema-vs-others',
        IsActive = N'Y'
    WHERE MenuItemID = 500;

    /* Consolidate Regular Pricing and How We Bill into Pricing & Invoicing. */
    UPDATE dbo.MenuItem
    SET ScreenTitle = N'Pricing & Invoicing',
        URL = N'/pricing',
        IsActive = N'Y'
    WHERE MenuItemID = 700;

    UPDATE dbo.MenuItem
    SET MenuName = N'Pricing & Invoicing',
        ScreenTitle = N'Pricing & Invoicing',
        URL = N'/pricing',
        IsActive = N'Y'
    WHERE MenuItemID = 800;

    UPDATE dbo.MenuItem
    SET IsActive = N'N'
    WHERE MenuItemID = 1000;

    /* Normalize menu URLs that contained a literal question mark or inconsistent casing. */
    UPDATE dbo.MenuItem
    SET URL = N'/ready-to-get-started'
    WHERE MenuItemID IN (1970, 1980, 1990);

    UPDATE dbo.MenuItem
    SET URL = N'/enter-pads'
    WHERE MenuItemID = 23550;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
    BEGIN
        ROLLBACK TRANSACTION;
    END;

    THROW;
END CATCH;
GO

SELECT
    MenuItemID,
    MenuItemTypeID,
    MenuName,
    ScreenTitle,
    URL,
    IsActive
FROM dbo.MenuItem
WHERE MenuItemID IN
(
    500,
    700,
    800,
    1000,
    1970,
    1980,
    1990,
    23550
)
ORDER BY MenuItemID;
GO
