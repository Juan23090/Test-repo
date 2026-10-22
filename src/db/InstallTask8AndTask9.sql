/*
    EMA TASK 8 + TASK 9 DATABASE INSTALL
    Run this against the EMA development database in SSMS.
    It does not remove any Task 8.1 tables.
*/

PRINT 'Installing TrustedLogins...';
GO

/*
    TRUSTED LOGIN DEVICES
    ---------------------
    Stores the normalized email address and browser device token used to skip
    repeat email 2FA on a previously verified device.

    The script is idempotent and can be run more than once.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(
    'dbo.TrustedLogins',
    'U'
) IS NULL
BEGIN
    CREATE TABLE dbo.TrustedLogins
    (
        EmailAddress       NVARCHAR(256)   NOT NULL,
        DeviceToken        UNIQUEIDENTIFIER NOT NULL,
        DateTimeCreated    DATETIME2(7)    NOT NULL
            CONSTRAINT DF_TrustedLogins_DateTimeCreated
                DEFAULT SYSUTCDATETIME(),
        DateTimeUpdated    DATETIME2(7)    NOT NULL
            CONSTRAINT DF_TrustedLogins_DateTimeUpdated
                DEFAULT SYSUTCDATETIME(),

        CONSTRAINT PK_TrustedLogins
            PRIMARY KEY
            (
                EmailAddress,
                DeviceToken
            )
    );
END;
GO

IF COL_LENGTH(
    'dbo.TrustedLogins',
    'DateTimeCreated'
) IS NULL
BEGIN
    ALTER TABLE dbo.TrustedLogins
        ADD DateTimeCreated DATETIME2(7) NULL;

    UPDATE dbo.TrustedLogins
    SET DateTimeCreated = SYSUTCDATETIME()
    WHERE DateTimeCreated IS NULL;

    ALTER TABLE dbo.TrustedLogins
        ALTER COLUMN DateTimeCreated DATETIME2(7) NOT NULL;
END;
GO

IF COL_LENGTH(
    'dbo.TrustedLogins',
    'DateTimeUpdated'
) IS NULL
BEGIN
    ALTER TABLE dbo.TrustedLogins
        ADD DateTimeUpdated DATETIME2(7) NULL;

    UPDATE dbo.TrustedLogins
    SET DateTimeUpdated =
        COALESCE(
            DateTimeCreated,
            SYSUTCDATETIME()
        )
    WHERE DateTimeUpdated IS NULL;

    ALTER TABLE dbo.TrustedLogins
        ALTER COLUMN DateTimeUpdated DATETIME2(7) NOT NULL;
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id =
          OBJECT_ID('dbo.TrustedLogins')
      AND name =
          'UX_TrustedLogins_EmailAddress_DeviceToken'
)
BEGIN
    CREATE UNIQUE INDEX UX_TrustedLogins_EmailAddress_DeviceToken
        ON dbo.TrustedLogins
        (
            EmailAddress,
            DeviceToken
        );
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id =
          OBJECT_ID('dbo.TrustedLogins')
      AND name =
          'IX_TrustedLogins_DeviceToken'
)
BEGIN
    CREATE INDEX IX_TrustedLogins_DeviceToken
        ON dbo.TrustedLogins
        (
            DeviceToken
        )
        INCLUDE
        (
            EmailAddress,
            DateTimeUpdated
        );
END;
GO


PRINT 'Installing AuthenticationSession...';
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.AuthenticationSession', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AuthenticationSession
    (
        AuthenticationSessionID UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT DF_AuthenticationSession_ID DEFAULT (NEWID()),

        SessionTokenHash VARBINARY(32) NOT NULL,

        UserID INT NULL,
        DonorID INT NULL,
        EMAOrganizationID INT NOT NULL,
        IsDonorSession BIT NOT NULL,

        DateTimeCreated DATETIME2(7) NOT NULL
            CONSTRAINT DF_AuthenticationSession_Created DEFAULT (SYSUTCDATETIME()),

        DateTimeUpdated DATETIME2(7) NOT NULL
            CONSTRAINT DF_AuthenticationSession_Updated DEFAULT (SYSUTCDATETIME()),

        ExpiresAtUtc DATETIME2(7) NOT NULL,
        AbsoluteExpiresAtUtc DATETIME2(7) NOT NULL,
        RevokedAtUtc DATETIME2(7) NULL,

        CONSTRAINT PK_AuthenticationSession
            PRIMARY KEY (AuthenticationSessionID),

        CONSTRAINT UQ_AuthenticationSession_TokenHash
            UNIQUE (SessionTokenHash),

        CONSTRAINT CK_AuthenticationSession_Actor
            CHECK
            (
                (IsDonorSession = 1 AND DonorID IS NOT NULL AND UserID IS NULL)
                OR
                (IsDonorSession = 0 AND UserID IS NOT NULL AND DonorID IS NULL)
            )
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.AuthenticationSession')
      AND name = N'IX_AuthenticationSession_ActiveExpiry'
)
BEGIN
    CREATE INDEX IX_AuthenticationSession_ActiveExpiry
        ON dbo.AuthenticationSession
        (
            RevokedAtUtc,
            ExpiresAtUtc,
            AbsoluteExpiresAtUtc
        )
        INCLUDE
        (
            UserID,
            DonorID,
            EMAOrganizationID,
            IsDonorSession,
            DateTimeUpdated
        );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.AuthenticationSession')
      AND name = N'IX_AuthenticationSession_OrganizationActor'
)
BEGIN
    CREATE INDEX IX_AuthenticationSession_OrganizationActor
        ON dbo.AuthenticationSession
        (
            EMAOrganizationID,
            IsDonorSession,
            UserID,
            DonorID
        )
        INCLUDE
        (
            RevokedAtUtc,
            ExpiresAtUtc,
            DateTimeUpdated
        );
END;

COMMIT TRANSACTION;


GO
PRINT 'Installing Ready to Get Started menu item...';
GO

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


GO
PRINT 'EMA Task 8 + Task 9 database installation completed.';
GO
