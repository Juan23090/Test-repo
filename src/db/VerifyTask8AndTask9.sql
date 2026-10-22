SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.TrustedLogins', N'U') IS NULL
    THROW 51000, 'dbo.TrustedLogins is missing.', 1;

IF OBJECT_ID(N'dbo.AuthenticationSession', N'U') IS NULL
    THROW 51000, 'dbo.AuthenticationSession is missing.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.MenuItem
    WHERE URL = N'/ready-to-get-started'
      AND IsActive = N'Y'
)
    THROW 51000, 'The Ready to Get Started menu item is missing or inactive.', 1;

SELECT
    N'TrustedLogins' AS CheckName,
    COUNT_BIG(*) AS CurrentRows
FROM dbo.TrustedLogins

UNION ALL

SELECT
    N'AuthenticationSession',
    COUNT_BIG(*)
FROM dbo.AuthenticationSession;

SELECT
    MenuItemCategoryID,
    MenuItemRowID,
    MenuItemID,
    MenuItemTypeID,
    MenuName,
    ScreenTitle,
    URL,
    IsActive
FROM dbo.MenuItem
WHERE URL = N'/ready-to-get-started';

SELECT TOP 20
    AuthenticationSessionID,
    UserID,
    DonorID,
    EMAOrganizationID,
    IsDonorSession,
    DateTimeCreated,
    DateTimeUpdated,
    ExpiresAtUtc,
    AbsoluteExpiresAtUtc,
    RevokedAtUtc
FROM dbo.AuthenticationSession
ORDER BY DateTimeUpdated DESC;

PRINT 'TASK 8 + TASK 9 DATABASE PREFLIGHT PASSED.';
