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
