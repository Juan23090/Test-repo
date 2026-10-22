/*
Task 22 - Onboarding Screens: Donation Attributes

This script records the database changes reviewed/applied for Task 22.
It intentionally does not create, drop, or modify foreign keys, cascade
settings, MenuItemUsage relationships, triggers, or existing MenuItems.

The application patch does NOT execute this script automatically.
*/

SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    /* Credit / Debit Card onboarding defaults requested by Task 22. */
    IF COL_LENGTH('dbo.Organization', 'EMAEventIDForCreditDebitCards') IS NULL
        ALTER TABLE dbo.Organization ADD EMAEventIDForCreditDebitCards INT NULL;

    IF COL_LENGTH('dbo.Organization', 'EMASourceIDForCreditDebitCards') IS NULL
        ALTER TABLE dbo.Organization ADD EMASourceIDForCreditDebitCards INT NULL;

    IF COL_LENGTH('dbo.Organization', 'EMAMethodOfGivingIDForCreditDebitCards') IS NULL
        ALTER TABLE dbo.Organization ADD EMAMethodOfGivingIDForCreditDebitCards INT NULL;

    IF COL_LENGTH('dbo.Organization', 'EMAFundIDForCreditDebitCards') IS NULL
        ALTER TABLE dbo.Organization ADD EMAFundIDForCreditDebitCards INT NULL;

    IF COL_LENGTH('dbo.Organization', 'EMACampusIDForCreditDebitCards') IS NULL
        ALTER TABLE dbo.Organization ADD EMACampusIDForCreditDebitCards INT NULL;

    /*
       PAD setup is a Wizard-only MenuItem. Fail rather than overwrite if the
       reserved ID and route disagree with the reviewed Task 22 configuration.
    */
    IF EXISTS
    (
        SELECT 1
        FROM dbo.MenuItem
        WHERE MenuItemID = 19815
          AND LOWER(LTRIM(RTRIM(ISNULL(URL, '')))) <> '/pads'
    )
        THROW 50001, 'MenuItemID 19815 is already assigned to another route.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.MenuItem
        WHERE LOWER(LTRIM(RTRIM(ISNULL(URL, '')))) = '/pads'
          AND MenuItemID <> 19815
    )
        THROW 50002, 'The /pads route is already assigned to another MenuItemID.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.MenuItem
        WHERE MenuItemID = 19815
          AND LOWER(LTRIM(RTRIM(ISNULL(URL, '')))) = '/pads'
    )
    BEGIN
        INSERT INTO dbo.MenuItem
        (
            MenuItemCategoryID,
            MenuItemRowID,
            MenuItemID,
            MenuItemTypeID,
            MenuName,
            ScreenTitle,
            DisplayControlID,
            URL,
            IsActive
        )
        VALUES
        (
            20,
            2,
            19815,
            11,
            'PADs',
            'Set Up Pre-Authorized Debits',
            'W',
            '/pads',
            'Y'
        );
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
