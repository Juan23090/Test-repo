SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.EmailSendLog', N'U') IS NULL
BEGIN
    THROW 53100, 'dbo.EmailSendLog does not exist.', 1;
END;

IF EXISTS
(
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.EmailSendLog')
      AND name = N'EMADonationID'
      AND is_nullable = 0
)
BEGIN
    ALTER TABLE dbo.EmailSendLog
        ALTER COLUMN EMADonationID BIGINT NULL;
END;

IF EXISTS
(
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.EmailSendLog')
      AND name = N'DonationStepID'
      AND is_nullable = 0
)
BEGIN
    ALTER TABLE dbo.EmailSendLog
        ALTER COLUMN DonationStepID INT NULL;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.EmailSendLog')
      AND name = N'CK_EmailSendLog_DonationContext'
)
BEGIN
    ALTER TABLE dbo.EmailSendLog WITH CHECK
        ADD CONSTRAINT CK_EmailSendLog_DonationContext
        CHECK
        (
            (EMADonationID IS NULL AND DonationStepID IS NULL)
            OR
            (EMADonationID IS NOT NULL AND DonationStepID IS NOT NULL)
        );
END;

COMMIT TRANSACTION;
GO
