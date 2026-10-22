USE [emasqldatabase-dev];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
    TRUSTED DEVICE COMPATIBILITY
    ----------------------------
    Creates Gabe's secure trusted-device table when it is missing.

    The API compatibility update will then perform a one-time migration of a
    validated legacy TrustedDeviceToken/dbo.TrustedLogins record into this
    hashed 90-day trusted-device model.

    This script is idempotent and can be run more than once.
*/

IF OBJECT_ID(
    N'dbo.TrustedLoginDevice',
    N'U'
) IS NULL
BEGIN
    CREATE TABLE dbo.TrustedLoginDevice
    (
        TrustedLoginDeviceID BIGINT IDENTITY(1, 1)
            NOT NULL,
        EmailAddress NVARCHAR(320)
            NOT NULL,
        DeviceTokenHash BINARY(32)
            NOT NULL,
        DateTimeCreated DATETIME2(7)
            NOT NULL
            CONSTRAINT DF_TrustedLoginDevice_DateTimeCreated
                DEFAULT SYSUTCDATETIME(),
        LastUsedAtUtc DATETIME2(7)
            NOT NULL
            CONSTRAINT DF_TrustedLoginDevice_LastUsedAtUtc
                DEFAULT SYSUTCDATETIME(),
        ExpiresAtUtc DATETIME2(7)
            NOT NULL,
        RevokedAtUtc DATETIME2(7)
            NULL,

        CONSTRAINT PK_TrustedLoginDevice
            PRIMARY KEY
            (
                TrustedLoginDeviceID
            )
    );

    PRINT 'Created dbo.TrustedLoginDevice.';
END
ELSE
BEGIN
    PRINT 'dbo.TrustedLoginDevice already exists.';
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id =
          OBJECT_ID(
              N'dbo.TrustedLoginDevice')
      AND name =
          N'UX_TrustedLoginDevice_Email_TokenHash'
)
BEGIN
    CREATE UNIQUE INDEX
        UX_TrustedLoginDevice_Email_TokenHash
        ON dbo.TrustedLoginDevice
        (
            EmailAddress,
            DeviceTokenHash
        );
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id =
          OBJECT_ID(
              N'dbo.TrustedLoginDevice')
      AND name =
          N'IX_TrustedLoginDevice_TokenHash'
)
BEGIN
    CREATE INDEX
        IX_TrustedLoginDevice_TokenHash
        ON dbo.TrustedLoginDevice
        (
            DeviceTokenHash
        )
        INCLUDE
        (
            EmailAddress,
            ExpiresAtUtc,
            RevokedAtUtc
        );
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id =
          OBJECT_ID(
              N'dbo.TrustedLoginDevice')
      AND name =
          N'IX_TrustedLoginDevice_Expiry'
)
BEGIN
    CREATE INDEX
        IX_TrustedLoginDevice_Expiry
        ON dbo.TrustedLoginDevice
        (
            ExpiresAtUtc
        )
        INCLUDE
        (
            RevokedAtUtc
        );
END;
GO

SELECT
    OBJECT_ID(
        N'dbo.TrustedLoginDevice',
        N'U') AS TrustedLoginDeviceObjectID,
    CASE
        WHEN OBJECT_ID(
                 N'dbo.TrustedLogins',
                 N'U') IS NOT NULL
            THEN N'Legacy table available for one-time migration'
        ELSE N'No legacy table present; new trusted-device registrations still work'
    END AS LegacyMigrationStatus;
GO
