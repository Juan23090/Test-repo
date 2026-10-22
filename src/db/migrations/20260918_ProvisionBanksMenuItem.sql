/*
    Provisions MenuItem 91975 (/banks) under EMA Insiders and grants access to Role 9000.
    Includes collision guards for MenuItemID and URL.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    -- Prerequisites
    IF OBJECT_ID(N'dbo.MenuItemCategory', N'U') IS NULL
        THROW 51600, N'dbo.MenuItemCategory is required before Banks menu can be provisioned.', 1;

    IF OBJECT_ID(N'dbo.MenuItem', N'U') IS NULL
        THROW 51601, N'dbo.MenuItem is required before Banks menu can be provisioned.', 1;

    IF OBJECT_ID(N'dbo.RoleMenuItem', N'U') IS NULL
        THROW 51602, N'dbo.RoleMenuItem is required before Banks menu can be provisioned.', 1;

    IF OBJECT_ID(N'dbo.[Role]', N'U') IS NULL
        THROW 51603, N'dbo.Role is required before Banks menu can be provisioned.', 1;

    IF NOT EXISTS (SELECT 1 FROM dbo.MenuItemCategory WHERE MenuItemCategoryID = 90)
        THROW 51604, N'MenuItemCategoryID 90 (EMA Insiders) does not exist. Deployment halted.', 1;

    IF NOT EXISTS (SELECT 1 FROM dbo.[Role] WHERE RoleID = 9000)
        THROW 51605, N'RoleID 9000 (Insider with Everything) does not exist. Deployment halted.', 1;

    -- Make sure MenuItemID 91975 isn't already assigned to a different URL
    IF EXISTS
    (
        SELECT 1
        FROM dbo.MenuItem
        WHERE MenuItemID = 91975
          AND LOWER(
                CASE
                    WHEN LEFT(LTRIM(RTRIM(COALESCE(URL, ''))), 1) = '/'
                        THEN LTRIM(RTRIM(COALESCE(URL, '')))
                    ELSE '/' + LTRIM(RTRIM(COALESCE(URL, '')))
                END
              ) <> '/banks'
    )
    BEGIN
        THROW 51606, N'MenuItemID 91975 already belongs to a different URL. Deployment halted.', 1;
    END;

    -- Make sure /banks isn't already using a different MenuItemID
    IF EXISTS
    (
        SELECT 1
        FROM dbo.MenuItem
        WHERE MenuItemID <> 91975
          AND LOWER(
                CASE
                    WHEN LEFT(LTRIM(RTRIM(COALESCE(URL, ''))), 1) = '/'
                        THEN LTRIM(RTRIM(COALESCE(URL, '')))
                    ELSE '/' + LTRIM(RTRIM(COALESCE(URL, '')))
                END
              ) = '/banks'
    )
    BEGIN
        THROW 51607, N'The /banks route already belongs to a different MenuItemID. Deployment halted.', 1;
    END;

    -- Insert or update the Banks menu item
    IF NOT EXISTS (SELECT 1 FROM dbo.MenuItem WHERE MenuItemID = 91975)
    BEGIN
        INSERT INTO dbo.MenuItem
            (MenuItemCategoryID, MenuItemRowID, MenuItemID, MenuItemTypeID,
             MenuName, ScreenTitle, DisplayControlID, URL, IsActive)
        VALUES
            (90, 2, 91975, 11, 'Banks', 'Banks', 'M', '/banks', 'Y');
    END
    ELSE
    BEGIN
        UPDATE dbo.MenuItem
        SET MenuItemCategoryID = 90,
            MenuItemRowID = 2,
            MenuItemTypeID = 11,
            MenuName = 'Banks',
            ScreenTitle = 'Banks',
            DisplayControlID = 'M',
            URL = '/banks',
            IsActive = 'Y'
        WHERE MenuItemID = 91975;
    END;

    -- Grant access to Role 9000
    IF NOT EXISTS (SELECT 1 FROM dbo.RoleMenuItem WHERE RoleID = 9000 AND MenuItemID = 91975)
    BEGIN
        INSERT INTO dbo.RoleMenuItem (RoleID, MenuItemID)
        VALUES (9000, 91975);
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
