SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @ReadyUrl NVARCHAR(250) =
    N'/ready-to-get-started';

DECLARE @MenuItemID INT =
(
    SELECT TOP (1)
        MenuItemID
    FROM dbo.MenuItem
    WHERE URL = @ReadyUrl
    ORDER BY MenuItemID
);

IF @MenuItemID IS NULL
BEGIN
    SELECT TOP (1)
        @MenuItemID =
            candidate.MenuItemID
    FROM
    (
        VALUES
            (1990),
            (1980),
            (1970),
            (1960),
            (1950)
    ) candidate(MenuItemID)
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.MenuItem existing
        WHERE existing.MenuItemID =
              candidate.MenuItemID
    )
    ORDER BY candidate.MenuItemID DESC;
END;

IF @MenuItemID IS NULL
BEGIN
    THROW 51000,
        'No reserved Ready to Get Started MenuItemID is available. Choose an unused Brochure menu ID and update this script.',
        1;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.MenuItem
    WHERE MenuItemID = @MenuItemID
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
        URL,
        IsActive
    )
    VALUES
    (
        10,
        1,
        @MenuItemID,
        1,
        N'Get Started',
        N'Ready to Get Started?',
        @ReadyUrl,
        N'Y'
    );
END
ELSE
BEGIN
    UPDATE dbo.MenuItem
    SET
        MenuItemCategoryID = 10,
        MenuItemRowID = 1,
        MenuItemTypeID = 1,
        MenuName = N'Get Started',
        ScreenTitle = N'Ready to Get Started?',
        URL = @ReadyUrl,
        IsActive = N'Y'
    WHERE MenuItemID = @MenuItemID;
END;

INSERT INTO dbo.RoleMenuItem
(
    RoleID,
    MenuItemID
)
SELECT
    r.RoleID,
    @MenuItemID
FROM dbo.[Role] r
WHERE ISNULL(r.IsActive, N'Y') = N'Y'
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.RoleMenuItem rmi
      WHERE rmi.RoleID = r.RoleID
        AND rmi.MenuItemID = @MenuItemID
  );

SELECT
    @MenuItemID AS ReadyToGetStartedMenuItemID;
