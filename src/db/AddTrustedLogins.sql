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
