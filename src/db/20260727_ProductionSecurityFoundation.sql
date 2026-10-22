SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

IF OBJECT_ID(
       N'dbo.OrganizationUserRole',
       N'U'
   ) IS NULL
BEGIN
    CREATE TABLE dbo.OrganizationUserRole
    (
        EMAOrganizationID INT NOT NULL,
        UserID INT NOT NULL,
        RoleID INT NOT NULL,
        DateTimeCreated DATETIME2(0) NOT NULL
            CONSTRAINT DF_OrganizationUserRole_DateTimeCreated
            DEFAULT SYSUTCDATETIME(),

        CONSTRAINT PK_OrganizationUserRole
            PRIMARY KEY
            (
                EMAOrganizationID,
                UserID,
                RoleID
            ),

        CONSTRAINT FK_OrganizationUserRole_Organization
            FOREIGN KEY
            (
                EMAOrganizationID
            )
            REFERENCES dbo.Organization
            (
                EMAOrganizationID
            ),

        CONSTRAINT FK_OrganizationUserRole_User
            FOREIGN KEY
            (
                UserID
            )
            REFERENCES dbo.[User]
            (
                UserID
            ),

        CONSTRAINT FK_OrganizationUserRole_Role
            FOREIGN KEY
            (
                RoleID
            )
            REFERENCES dbo.[Role]
            (
                RoleID
            )
    );
END;

-- Preserve the current effective access while moving role assignments from
-- global user scope to organization membership scope.
INSERT INTO dbo.OrganizationUserRole
(
    EMAOrganizationID,
    UserID,
    RoleID
)
SELECT DISTINCT
    organizationUser.EMAOrganizationID,
    userRole.UserID,
    userRole.RoleID
FROM dbo.OrganizationUser organizationUser
INNER JOIN dbo.UserRoles userRole
    ON userRole.UserID =
       organizationUser.UserID
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.OrganizationUserRole existingRole
    WHERE existingRole.EMAOrganizationID =
          organizationUser.EMAOrganizationID
      AND existingRole.UserID =
          userRole.UserID
      AND existingRole.RoleID =
          userRole.RoleID
);

IF NOT EXISTS
(
    SELECT 1
    FROM sys.sequences
    WHERE name =
          N'OrganizationUserIDSequence'
      AND schema_id =
          SCHEMA_ID(N'dbo')
)
BEGIN
    DECLARE @OrganizationUserSequenceStart BIGINT =
    (
        SELECT
            ISNULL(
                MAX(
                    CONVERT(
                        BIGINT,
                        OrganizationUserID)),
                0) + 1
        FROM dbo.OrganizationUser
    );

    DECLARE @CreateOrganizationUserSequenceSql
        NVARCHAR(MAX) =
            N'CREATE SEQUENCE dbo.OrganizationUserIDSequence '
            + N'AS INT START WITH '
            + CONVERT(
                NVARCHAR(20),
                @OrganizationUserSequenceStart)
            + N' INCREMENT BY 1;';

    EXEC sys.sp_executesql
        @CreateOrganizationUserSequenceSql;
END;

IF OBJECT_ID(
       N'dbo.ApiSession',
       N'U'
   ) IS NULL
BEGIN
    CREATE TABLE dbo.ApiSession
    (
        ApiSessionID UNIQUEIDENTIFIER NOT NULL,
        SecurityStamp UNIQUEIDENTIFIER NOT NULL,
        ActorType VARCHAR(32) NOT NULL,
        EMAOrganizationID INT NOT NULL,
        UserID INT NOT NULL,
        DonorID INT NULL,
        IssuedAtUtc DATETIME2(0) NOT NULL,
        ExpiresAtUtc DATETIME2(0) NOT NULL,
        RevokedAtUtc DATETIME2(0) NULL,

        CONSTRAINT PK_ApiSession
            PRIMARY KEY
            (
                ApiSessionID
            ),

        CONSTRAINT CK_ApiSession_ActorType
            CHECK
            (
                ActorType IN
                (
                    'OrganizationUser',
                    'Donor'
                )
            ),

        CONSTRAINT CK_ApiSession_ActorShape
            CHECK
            (
                (
                    ActorType =
                    'OrganizationUser'
                    AND DonorID IS NULL
                )
                OR
                (
                    ActorType =
                    'Donor'
                    AND DonorID IS NOT NULL
                    AND DonorID > 0
                )
            ),

        CONSTRAINT CK_ApiSession_Expiry
            CHECK
            (
                ExpiresAtUtc >
                IssuedAtUtc
            )
    );

    CREATE UNIQUE INDEX UX_ApiSession_SecurityStamp
        ON dbo.ApiSession
        (
            SecurityStamp
        );

    CREATE INDEX IX_ApiSession_ActiveUser
        ON dbo.ApiSession
        (
            EMAOrganizationID,
            UserID,
            ExpiresAtUtc
        )
        INCLUDE
        (
            ApiSessionID,
            SecurityStamp,
            ActorType,
            RevokedAtUtc
        );

    CREATE INDEX IX_ApiSession_ActiveDonor
        ON dbo.ApiSession
        (
            EMAOrganizationID,
            DonorID,
            ExpiresAtUtc
        )
        INCLUDE
        (
            ApiSessionID,
            SecurityStamp,
            ActorType,
            RevokedAtUtc
        )
        WHERE DonorID IS NOT NULL;
END;

IF OBJECT_ID(
       N'dbo.ApiRequestRateLimit',
       N'U'
   ) IS NULL
BEGIN
    CREATE TABLE dbo.ApiRequestRateLimit
    (
        RateLimitKey BINARY(32) NOT NULL,
        FunctionName NVARCHAR(128) NOT NULL,
        WindowStartedAtUtc DATETIME2(0) NOT NULL,
        RequestCount INT NOT NULL,

        CONSTRAINT PK_ApiRequestRateLimit
            PRIMARY KEY
            (
                RateLimitKey,
                FunctionName,
                WindowStartedAtUtc
            ),

        CONSTRAINT CK_ApiRequestRateLimit_RequestCount
            CHECK
            (
                RequestCount > 0
            )
    );

    CREATE INDEX IX_ApiRequestRateLimit_Cleanup
        ON dbo.ApiRequestRateLimit
        (
            WindowStartedAtUtc
        );
END;

IF OBJECT_ID(
       N'dbo.SandboxCapability',
       N'U'
   ) IS NULL
BEGIN
    CREATE TABLE dbo.SandboxCapability
    (
        SandboxOrganizationID INT NOT NULL,
        CapabilityHash BINARY(32) NOT NULL,
        CreatedAtUtc DATETIME2(0) NOT NULL
            CONSTRAINT DF_SandboxCapability_CreatedAtUtc
            DEFAULT SYSUTCDATETIME(),
        RevokedAtUtc DATETIME2(0) NULL,

        CONSTRAINT PK_SandboxCapability
            PRIMARY KEY
            (
                SandboxOrganizationID
            ),

        CONSTRAINT UQ_SandboxCapability_Hash
            UNIQUE
            (
                CapabilityHash
            ),

        CONSTRAINT FK_SandboxCapability_Organization
            FOREIGN KEY
            (
                SandboxOrganizationID
            )
            REFERENCES dbo.Organization
            (
                EMAOrganizationID
            )
    );
END;

IF OBJECT_ID(
       N'dbo.PendingDonorRegistration',
       N'U'
   ) IS NULL
BEGIN
    CREATE TABLE dbo.PendingDonorRegistration
    (
        RegistrationID CHAR(32) NOT NULL,
        EMAOrganizationID INT NOT NULL,
        DestinationHash BINARY(32) NOT NULL,
        CodeHash BINARY(32) NOT NULL,
        EncryptedPayload VARBINARY(MAX) NOT NULL,
        EncryptionNonce BINARY(12) NOT NULL,
        AuthenticationTag BINARY(16) NOT NULL,
        FailedAttempts INT NOT NULL,
        CreatedAtUtc DATETIME2(0) NOT NULL,
        ExpiresAtUtc DATETIME2(0) NOT NULL,
        ConsumedAtUtc DATETIME2(0) NULL,

        CONSTRAINT PK_PendingDonorRegistration
            PRIMARY KEY
            (
                RegistrationID
            ),

        CONSTRAINT FK_PendingDonorRegistration_Organization
            FOREIGN KEY
            (
                EMAOrganizationID
            )
            REFERENCES dbo.Organization
            (
                EMAOrganizationID
            ),

        CONSTRAINT CK_PendingDonorRegistration_Attempts
            CHECK
            (
                FailedAttempts >= 0
                AND FailedAttempts <= 5
            ),

        CONSTRAINT CK_PendingDonorRegistration_Expiry
            CHECK
            (
                ExpiresAtUtc >
                CreatedAtUtc
            )
    );

    CREATE INDEX IX_PendingDonorRegistration_Destination
        ON dbo.PendingDonorRegistration
        (
            DestinationHash,
            CreatedAtUtc
        );

    CREATE INDEX IX_PendingDonorRegistration_Cleanup
        ON dbo.PendingDonorRegistration
        (
            ExpiresAtUtc
        );
END;

COMMIT TRANSACTION;
GO
