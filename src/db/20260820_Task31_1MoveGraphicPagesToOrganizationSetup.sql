/*
    Task 31.1 - Move the three graphic workflow pages to Organization / Setup

    Roger requested that these three graphic screens be kept on the
    Organization menu - Setup and no longer be treated as public brochure
    pages:

        19765 - eTransfers by eMail Ingestion      - /how-it-works
        19770 - eTransfers from Bank Statements   - /stmt-etransfers
        19775 - PADS from Bank Statements         - /pads-from-bank-stmts

    IMPORTANT:
    This is a database data change. The application patch does NOT execute
    this script automatically. Review the exact SQL before running it.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.MenuItem
        WHERE MenuItemID = 19765
          AND MenuItemCategoryID = 20
          AND MenuItemRowID = 2
          AND MenuItemTypeID = 11
    )
        THROW 50001, 'Expected Organization/Setup MenuItem 19765 was not found.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.MenuItem
        WHERE MenuItemID = 19770
          AND MenuItemCategoryID = 20
          AND MenuItemRowID = 2
          AND MenuItemTypeID = 11
    )
        THROW 50002, 'Expected Organization/Setup MenuItem 19770 was not found.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.MenuItem
        WHERE MenuItemID = 19775
          AND MenuItemCategoryID = 20
          AND MenuItemRowID = 2
          AND MenuItemTypeID = 11
    )
        THROW 50003, 'Expected Organization/Setup MenuItem 19775 was not found.', 1;

    /*
       Task 31.1 supersedes the earlier public PADS menu plan. If 10270 was
       created exactly for that earlier proposal, disable it before route
       ownership is checked. Never repurpose an unrelated 10270 record.
    */
    IF EXISTS
    (
        SELECT 1
        FROM dbo.MenuItem
        WHERE MenuItemID = 10270
          AND
          (
              MenuItemCategoryID <> 10
              OR LOWER(LTRIM(RTRIM(ISNULL(URL, '')))) <> '/pads-from-bank-stmts'
          )
    )
        THROW 50004, 'MenuItemID 10270 exists but is not the superseded public PADS item. No changes were applied.', 1;

    UPDATE dbo.MenuItem
    SET IsActive = 'N'
    WHERE MenuItemID = 10270
      AND MenuItemCategoryID = 10
      AND LOWER(LTRIM(RTRIM(ISNULL(URL, '')))) = '/pads-from-bank-stmts';

    /* Fail rather than creating duplicate active route ownership. */
    IF EXISTS
    (
        SELECT 1
        FROM dbo.MenuItem
        WHERE MenuItemID NOT IN (19765, 19770, 19775)
          AND IsActive = 'Y'
          AND LOWER(LTRIM(RTRIM(ISNULL(URL, '')))) IN
              ('/how-it-works', '/stmt-etransfers', '/pads-from-bank-stmts')
    )
        THROW 50005, 'One of the graphic workflow routes is already owned by another active MenuItem.', 1;

    UPDATE dbo.MenuItem
    SET
        MenuName = 'Process',
        ScreenTitle = 'eTransfers by eMail Ingestion',
        DisplayControlID = 'M',
        URL = '/how-it-works',
        IsActive = 'Y'
    WHERE MenuItemID = 19765;

    UPDATE dbo.MenuItem
    SET
        MenuName = 'Stmt eTransfers',
        ScreenTitle = 'eTransfers from Bank Statements',
        DisplayControlID = 'M',
        URL = '/stmt-etransfers',
        IsActive = 'Y'
    WHERE MenuItemID = 19770;

    UPDATE dbo.MenuItem
    SET
        MenuName = 'PADS',
        ScreenTitle = 'PADS from Bank Statements',
        DisplayControlID = 'M',
        URL = '/pads-from-bank-stmts',
        IsActive = 'Y'
    WHERE MenuItemID = 19775;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
