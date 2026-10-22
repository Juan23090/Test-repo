/*
    SECURE TRUSTED LOGIN DEVICES
    ----------------------------
    Stores only SHA-256 hashes of opaque browser tokens. A trusted device
    expires after 90 days and can be revoked server-side without changing
    the user's authentication session.

    This script is idempotent and can be run more than once.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

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
