-- src/db/insert_mailing_lists_menu.sql
-- Configure the Mailing Lists index and its related organization screens.

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @MailingListsMenuItemID INT =
    (
        SELECT TOP (1)
            MenuItemID
        FROM dbo.MenuItem
        WHERE URL = '/mailing-lists'
          AND IsActive = 'Y'
        ORDER BY MenuItemID
    );

    IF @MailingListsMenuItemID IS NULL
    BEGIN
        THROW 50001,
            'The active /mailing-lists parent menu item was not found.',
            1;
    END;

    /*
        Create the visible Mailing Lists Index menu item if it does
        not already exist.
    */
    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.MenuItem
        WHERE URL = '/mailing-lists-index'
    )
    BEGIN
        IF EXISTS
        (
            SELECT 1
            FROM dbo.MenuItem
            WHERE MenuItemID = 3500
        )
        BEGIN
            THROW 50002,
                'MenuItemID 3500 is already assigned to another menu item.',
                1;
        END;

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
            3500,
            11,
            'Mailing Lists Index',
            'Mailing Lists',
            'M',
            '/mailing-lists-index',
            'Y'
        );
    END;

    DECLARE @MailingListsIndexMenuItemID INT =
    (
        SELECT TOP (1)
            MenuItemID
        FROM dbo.MenuItem
        WHERE URL = '/mailing-lists-index'
        ORDER BY MenuItemID
    );

    /*
        Correct records created by the original deployment script.
        Category 20 is Organizations; category 30 is Donors.
    */
    UPDATE dbo.MenuItem
    SET MenuItemCategoryID = 20,
        MenuItemRowID = 2,
        MenuItemTypeID = 11,
        MenuName = 'Mailing Lists Index',
        ScreenTitle = 'Mailing Lists',
        DisplayControlID = 'M',
        IsActive = 'Y'
    WHERE MenuItemID = @MailingListsIndexMenuItemID;

    /*
        Give the index the same role access as the existing
        /mailing-lists parent item.
    */
    INSERT INTO dbo.RoleMenuItem
    (
        RoleID,
        MenuItemID
    )
    SELECT DISTINCT
        parentRole.RoleID,
        @MailingListsIndexMenuItemID
    FROM dbo.RoleMenuItem parentRole
    WHERE parentRole.MenuItemID = @MailingListsMenuItemID
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.RoleMenuItem indexRole
          WHERE indexRole.RoleID = parentRole.RoleID
            AND indexRole.MenuItemID =
                @MailingListsIndexMenuItemID
      );

    IF OBJECT_ID(N'dbo.MenuItemResource', N'U') IS NULL
    BEGIN
        THROW 50003,
            'dbo.MenuItemResource is unavailable.',
            1;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.MenuItemResourceType
        WHERE UPPER(MenuItemResourceTypeID) = 'W'
    )
    BEGIN
        THROW 50004,
            'The Wrench MenuItemResourceType is unavailable.',
            1;
    END;

    DECLARE @BaseSequence INT =
    (
        SELECT ISNULL(MAX(Sequence), 0)
        FROM dbo.MenuItemResource
        WHERE MenuItemID = @MailingListsMenuItemID
    );

    DECLARE @RequiredRoutes TABLE
    (
        RouteOrder INT NOT NULL,
        Description VARCHAR(256) NOT NULL,
        ScreenURL VARCHAR(256) NOT NULL
    );

    INSERT INTO @RequiredRoutes
    (
        RouteOrder,
        Description,
        ScreenURL
    )
    VALUES
        (
            1,
            'How Mailing Lists Work',
            '/mailing-lists-how-it-works'
        ),
        (
            2,
            'Create Mailing List',
            '/mailing-list-create'
        ),
        (
            3,
            'Mailing List Details',
            '/mailing-list-details'
        ),
        (
            4,
            'Review Mailing List Donors',
            '/mailing-list-review'
        );

    /*
        The access checker uses prefix matching, so:
          /mailing-list-details
        authorizes:
          /mailing-list-details/{id}

        The same applies to /mailing-list-review/{id}.
    */
    INSERT INTO dbo.MenuItemResource
    (
        MenuItemID,
        MenuItemResourceTypeID,
        Sequence,
        Description,
        AudioScript,
        AudioVideoBlob,
        ScreenURL
    )
    SELECT
        @MailingListsMenuItemID,
        'W',
        @BaseSequence + requiredRoute.RouteOrder,
        requiredRoute.Description,
        NULL,
        NULL,
        requiredRoute.ScreenURL
    FROM @RequiredRoutes requiredRoute
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.MenuItemResource existingResource
        WHERE existingResource.MenuItemID =
                  @MailingListsMenuItemID
          AND UPPER(existingResource.MenuItemResourceTypeID) = 'W'
          AND LOWER(LTRIM(RTRIM(existingResource.ScreenURL))) =
              LOWER(requiredRoute.ScreenURL)
    );

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
    BEGIN
        ROLLBACK TRANSACTION;
    END;

    THROW;
END CATCH;