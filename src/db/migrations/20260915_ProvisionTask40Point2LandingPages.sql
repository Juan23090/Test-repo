/*
    Task 40.2 - Deployable landing-page configuration
    -------------------------------------------------
    Provisions the lookup values, dedicated landing MenuItems, and the
    minimum intended RoleMenuItem grants required by the Task 40.2 code.

    IMPORTANT:
    - This script mutates database configuration/data.
    - Per EMA governance, do not execute it in any environment until Roger
      Osness has reviewed/approved the exact script.
    - The script is idempotent and intentionally fails on ID/URL collisions
      or unknown active landing rows instead of repurposing data silently.
    - VerifyTask40Point2LandingPages.sql remains the read-only post-deploy
      verifier and should be run after an approved deployment.

    Intended dedicated landing records:
      10000 | Public       (10) | /pub20260906
      20000 | Organization (20) | /org20260906
      30000 | Donor        (30) | /don20260906
      90000 | Insider      (90) | /ins20260906

    Intended RoleMenuItem grants:
      Public 10000:       none (public brochure routing is not role-gated)
      Organization 20000: Role 10, Role 20
      Donor 30000:        Role 3000, Role 3010
      Insider 90000:      Role 9000
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.MenuItemRow', N'U') IS NULL
        THROW 51540, N'dbo.MenuItemRow is required before Task 40.2 landing pages can be provisioned.', 1;

    IF OBJECT_ID(N'dbo.MenuItemType', N'U') IS NULL
        THROW 51541, N'dbo.MenuItemType is required before Task 40.2 landing pages can be provisioned.', 1;

    IF OBJECT_ID(N'dbo.MenuItemCategory', N'U') IS NULL
        THROW 51542, N'dbo.MenuItemCategory is required before Task 40.2 landing pages can be provisioned.', 1;

    IF OBJECT_ID(N'dbo.MenuItem', N'U') IS NULL
        THROW 51543, N'dbo.MenuItem is required before Task 40.2 landing pages can be provisioned.', 1;

    IF OBJECT_ID(N'dbo.RoleMenuItem', N'U') IS NULL
        THROW 51544, N'dbo.RoleMenuItem is required before Task 40.2 landing pages can be provisioned.', 1;

    IF OBJECT_ID(N'dbo.[Role]', N'U') IS NULL
        THROW 51545, N'dbo.Role is required before Task 40.2 landing pages can be provisioned.', 1;

    /* Row 0 and Type 0 are semantic lookup values, not generic defaults. */
    IF EXISTS
    (
        SELECT 1
        FROM dbo.MenuItemRow
        WHERE MenuItemRowID = 0
          AND LOWER(LTRIM(RTRIM(COALESCE(Description, N'')))) <> N'landing page'
    )
        THROW 51546, N'MenuItemRowID 0 exists but is not Landing Page. Deployment halted.', 1;

    IF NOT EXISTS (SELECT 1 FROM dbo.MenuItemRow WHERE MenuItemRowID = 0)
    BEGIN
        INSERT INTO dbo.MenuItemRow (MenuItemRowID, Description)
        VALUES (0, N'Landing Page');
    END;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.MenuItemType
        WHERE MenuItemTypeID = 0
          AND LOWER(LTRIM(RTRIM(COALESCE(Description, N'')))) <> N'landing'
    )
        THROW 51547, N'MenuItemTypeID 0 exists but is not Landing. Deployment halted.', 1;

    IF NOT EXISTS (SELECT 1 FROM dbo.MenuItemType WHERE MenuItemTypeID = 0)
    BEGIN
        INSERT INTO dbo.MenuItemType (MenuItemTypeID, Description)
        VALUES (0, N'Landing');
    END;

    IF (SELECT COUNT(*) FROM dbo.MenuItemCategory WHERE MenuItemCategoryID IN (10,20,30,90)) <> 4
        THROW 51548, N'One or more Task 40.2 menu categories (10,20,30,90) are missing. Deployment halted.', 1;

    DECLARE @Landing TABLE
    (
        MenuItemID int NOT NULL PRIMARY KEY,
        MenuItemCategoryID smallint NOT NULL,
        URL varchar(255) NOT NULL,
        MenuName varchar(255) NOT NULL,
        ScreenTitle varchar(255) NOT NULL
    );

    INSERT INTO @Landing
        (MenuItemID, MenuItemCategoryID, URL, MenuName, ScreenTitle)
    VALUES
        (10000, 10, '/pub20260906', 'Public Landing', 'EMA Public Website'),
        (20000, 20, '/org20260906', 'Organization Landing', 'EMA Organization Website'),
        (30000, 30, '/don20260906', 'Donor Landing', 'EMA Donor Website'),
        (90000, 90, '/ins20260906', 'Insider Landing', 'EMA Insider Website');

    /*
        Guard MenuItem IDs first. An expected ID may be updated only when it
        already owns the expected route (leading slash/case differences are
        normalized). A different route means this ID belongs to something else.
    */
    IF EXISTS
    (
        SELECT 1
        FROM @Landing expected
        INNER JOIN dbo.MenuItem actual
            ON actual.MenuItemID = expected.MenuItemID
        WHERE LOWER(
                  CASE
                      WHEN LEFT(LTRIM(RTRIM(COALESCE(actual.URL, ''))), 1) = '/'
                          THEN LTRIM(RTRIM(COALESCE(actual.URL, '')))
                      ELSE '/' + LTRIM(RTRIM(COALESCE(actual.URL, '')))
                  END
              ) <> LOWER(expected.URL)
    )
        THROW 51549, N'A Task 40.2 MenuItemID already belongs to a different URL. Deployment halted.', 1;

    /* Guard routes independently so an existing URL is never stolen from another ID. */
    IF EXISTS
    (
        SELECT 1
        FROM @Landing expected
        INNER JOIN dbo.MenuItem actual
            ON LOWER(
                   CASE
                       WHEN LEFT(LTRIM(RTRIM(COALESCE(actual.URL, ''))), 1) = '/'
                           THEN LTRIM(RTRIM(COALESCE(actual.URL, '')))
                       ELSE '/' + LTRIM(RTRIM(COALESCE(actual.URL, '')))
                   END
               ) = LOWER(expected.URL)
        WHERE actual.MenuItemID <> expected.MenuItemID
    )
        THROW 51550, N'A Task 40.2 landing URL already belongs to a different MenuItemID. Deployment halted.', 1;

    /*
        Only the three known pre-40.2 landing copies may be retired
        automatically. Any other active L row is an environment-specific
        configuration and must be reviewed rather than silently disabled.
    */
    DECLARE @KnownLegacyLanding TABLE
    (
        MenuItemID int NOT NULL PRIMARY KEY,
        MenuItemCategoryID smallint NOT NULL,
        URL varchar(255) NOT NULL
    );

    INSERT INTO @KnownLegacyLanding (MenuItemID, MenuItemCategoryID, URL)
    VALUES
        (21010, 20, '/kpis'),
        (30110, 30, '/donor-dashboard'),
        (90050, 90, '/ema-insiders');

    IF EXISTS
    (
        SELECT 1
        FROM dbo.MenuItem actual
        WHERE actual.MenuItemCategoryID IN (10,20,30,90)
          AND actual.IsActive = 'Y'
          AND UPPER(LTRIM(RTRIM(COALESCE(actual.DisplayControlID, '')))) = 'L'
          AND NOT EXISTS
          (
              SELECT 1
              FROM @Landing expected
              WHERE expected.MenuItemID = actual.MenuItemID
          )
          AND NOT EXISTS
          (
              SELECT 1
              FROM @KnownLegacyLanding legacy
              WHERE legacy.MenuItemID = actual.MenuItemID
                AND legacy.MenuItemCategoryID = actual.MenuItemCategoryID
                AND LOWER(
                        CASE
                            WHEN LEFT(LTRIM(RTRIM(COALESCE(actual.URL, ''))), 1) = '/'
                                THEN LTRIM(RTRIM(COALESCE(actual.URL, '')))
                            ELSE '/' + LTRIM(RTRIM(COALESCE(actual.URL, '')))
                        END
                    ) = LOWER(legacy.URL)
          )
    )
        THROW 51551, N'An unknown active landing MenuItem already exists in a Task 40.2 category. Review it before deployment.', 1;

    /* Upsert the four dedicated landing records without overwriting useful existing labels. */
    UPDATE actual
    SET
        actual.MenuItemCategoryID = expected.MenuItemCategoryID,
        actual.MenuItemRowID = 0,
        actual.MenuItemTypeID = 0,
        actual.MenuName = CASE
            WHEN NULLIF(LTRIM(RTRIM(actual.MenuName)), '') IS NULL
                THEN expected.MenuName
            ELSE actual.MenuName
        END,
        actual.ScreenTitle = CASE
            WHEN NULLIF(LTRIM(RTRIM(actual.ScreenTitle)), '') IS NULL
                THEN expected.ScreenTitle
            ELSE actual.ScreenTitle
        END,
        actual.DisplayControlID = 'L',
        actual.URL = expected.URL,
        actual.IsActive = 'Y'
    FROM dbo.MenuItem actual
    INNER JOIN @Landing expected
        ON expected.MenuItemID = actual.MenuItemID;

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
    SELECT
        expected.MenuItemCategoryID,
        0,
        expected.MenuItemID,
        0,
        expected.MenuName,
        expected.ScreenTitle,
        'L',
        expected.URL,
        'Y'
    FROM @Landing expected
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.MenuItem actual
        WHERE actual.MenuItemID = expected.MenuItemID
    );

    /* Retire only the known legacy L copies when they still match their historical identity. */
    UPDATE actual
    SET actual.IsActive = 'N'
    FROM dbo.MenuItem actual
    INNER JOIN @KnownLegacyLanding legacy
        ON legacy.MenuItemID = actual.MenuItemID
       AND legacy.MenuItemCategoryID = actual.MenuItemCategoryID
       AND LOWER(
               CASE
                   WHEN LEFT(LTRIM(RTRIM(COALESCE(actual.URL, ''))), 1) = '/'
                       THEN LTRIM(RTRIM(COALESCE(actual.URL, '')))
                   ELSE '/' + LTRIM(RTRIM(COALESCE(actual.URL, '')))
               END
           ) = LOWER(legacy.URL)
    WHERE UPPER(LTRIM(RTRIM(COALESCE(actual.DisplayControlID, '')))) = 'L'
      AND actual.IsActive <> 'N';

    DECLARE @ExpectedRoleMenuItem TABLE
    (
        RoleID int NOT NULL,
        MenuItemID int NOT NULL,
        PRIMARY KEY (RoleID, MenuItemID)
    );

    INSERT INTO @ExpectedRoleMenuItem (RoleID, MenuItemID)
    VALUES
        (10,   20000),
        (20,   20000),
        (3000, 30000),
        (3010, 30000),
        (9000, 90000);

    IF EXISTS
    (
        SELECT 1
        FROM @ExpectedRoleMenuItem expected
        LEFT JOIN dbo.[Role] roleRow
            ON roleRow.RoleID = expected.RoleID
        WHERE roleRow.RoleID IS NULL
    )
        THROW 51552, N'An intended Task 40.2 role does not exist. Deployment halted before adding RoleMenuItem grants.', 1;

    /*
        Existing extra grants are not deleted silently. Treat them as an
        environment/security decision and stop so they can be reviewed.
        This also guarantees the public landing item has no role mappings.
    */
    IF EXISTS
    (
        SELECT 1
        FROM dbo.RoleMenuItem actual
        INNER JOIN @Landing landing
            ON landing.MenuItemID = actual.MenuItemID
        LEFT JOIN @ExpectedRoleMenuItem expected
            ON expected.RoleID = actual.RoleID
           AND expected.MenuItemID = actual.MenuItemID
        WHERE expected.RoleID IS NULL
    )
        THROW 51553, N'An unexpected RoleMenuItem grant exists on a Task 40.2 landing item. Review it before deployment.', 1;

    INSERT INTO dbo.RoleMenuItem (RoleID, MenuItemID)
    SELECT expected.RoleID, expected.MenuItemID
    FROM @ExpectedRoleMenuItem expected
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.RoleMenuItem actual
        WHERE actual.RoleID = expected.RoleID
          AND actual.MenuItemID = expected.MenuItemID
    );

    /* Final contract checks before commit. */
    IF EXISTS
    (
        SELECT 1
        FROM @Landing expected
        LEFT JOIN dbo.MenuItem actual
            ON actual.MenuItemID = expected.MenuItemID
        WHERE actual.MenuItemID IS NULL
           OR actual.MenuItemCategoryID <> expected.MenuItemCategoryID
           OR actual.MenuItemRowID <> 0
           OR actual.MenuItemTypeID <> 0
           OR UPPER(LTRIM(RTRIM(COALESCE(actual.DisplayControlID, '')))) <> 'L'
           OR actual.IsActive <> 'Y'
           OR LOWER(LTRIM(RTRIM(COALESCE(actual.URL, '')))) <> LOWER(expected.URL)
    )
        THROW 51554, N'Task 40.2 landing MenuItem post-deployment validation failed.', 1;

    IF EXISTS
    (
        SELECT category.MenuItemCategoryID
        FROM dbo.MenuItemCategory category
        LEFT JOIN dbo.MenuItem landing
            ON landing.MenuItemCategoryID = category.MenuItemCategoryID
           AND landing.IsActive = 'Y'
           AND UPPER(LTRIM(RTRIM(COALESCE(landing.DisplayControlID, '')))) = 'L'
        WHERE category.MenuItemCategoryID IN (10,20,30,90)
        GROUP BY category.MenuItemCategoryID
        HAVING COUNT(landing.MenuItemID) <> 1
    )
        THROW 51555, N'Each Task 40.2 category must have exactly one active landing item.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM @ExpectedRoleMenuItem expected
        LEFT JOIN dbo.RoleMenuItem actual
            ON actual.RoleID = expected.RoleID
           AND actual.MenuItemID = expected.MenuItemID
        WHERE actual.RoleID IS NULL
    )
        THROW 51556, N'Task 40.2 RoleMenuItem post-deployment validation failed.', 1;

    COMMIT TRANSACTION;

    PRINT 'Task 40.2 landing-page configuration provisioned successfully.';
    PRINT 'Run src/db/VerifyTask40Point2LandingPages.sql as the read-only post-deployment verifier.';
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
