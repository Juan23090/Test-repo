SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH('dbo.Organization', 'EtransferEmailVerified') IS NULL
BEGIN
    ALTER TABLE dbo.Organization
    ADD EtransferEmailVerified char(1) NOT NULL
        CONSTRAINT DF_Organization_EtransferEmailVerified DEFAULT ('N');
END;

IF OBJECT_ID('dbo.VerificationChallenge', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.VerificationChallenge
    (
        VerificationID char(32) NOT NULL,
        Purpose int NOT NULL,
        ActorID int NOT NULL,
        DestinationHash varbinary(32) NOT NULL,
        CodeHash varbinary(32) NOT NULL,
        FailedAttempts int NOT NULL
            CONSTRAINT DF_VerificationChallenge_FailedAttempts DEFAULT (0),
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        ExpiresAtUtc datetimeoffset(7) NOT NULL,
        VerifiedAtUtc datetimeoffset(7) NULL,
        ConsumedAtUtc datetimeoffset(7) NULL,

        CONSTRAINT PK_VerificationChallenge
            PRIMARY KEY CLUSTERED (VerificationID),

        CONSTRAINT CK_VerificationChallenge_FailedAttempts
            CHECK (FailedAttempts >= 0 AND FailedAttempts <= 5),

        CONSTRAINT CK_VerificationChallenge_Expiry
            CHECK (ExpiresAtUtc > CreatedAtUtc)
    );

    CREATE INDEX IX_VerificationChallenge_RateLimit
        ON dbo.VerificationChallenge
        (
            Purpose,
            ActorID,
            DestinationHash,
            CreatedAtUtc DESC
        );

    CREATE INDEX IX_VerificationChallenge_Expiry
        ON dbo.VerificationChallenge (ExpiresAtUtc);

    CREATE INDEX IX_VerificationChallenge_DestinationRateLimit
        ON dbo.VerificationChallenge
        (
            DestinationHash,
            CreatedAtUtc DESC
        );
END;

COMMIT TRANSACTION;