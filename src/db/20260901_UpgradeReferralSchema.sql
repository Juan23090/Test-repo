/*
    Referral Program Database Migration

    Brings a pre-existing dbo.Referral table up to the column names the API
    expects. It intentionally does NOT create the table -- run
    InstallReferralProgramSchema.sql first. See that file for the full run order.

    No stored procedures are defined here; dbo.sp_Referral_* live only in
    20260901_ReferralStoredProcedures.sql.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    -- 1. Ensure dbo.Referral table exists with proper schema
    IF OBJECT_ID(N'dbo.Referral', N'U') IS NOT NULL
    BEGIN
        -- Add columns if missing
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Referral') AND name = N'ReferringOrgID')
        BEGIN
            IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Referral') AND name = N'ReferringOrganizationID')
                EXEC sp_rename 'dbo.Referral.ReferringOrganizationID', 'ReferringOrgID', 'COLUMN';
            ELSE
                ALTER TABLE dbo.Referral ADD ReferringOrgID INT NULL;
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Referral') AND name = N'ReferredOrgName')
            ALTER TABLE dbo.Referral ADD ReferredOrgName NVARCHAR(255) NULL;

        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Referral') AND name = N'ContactName')
            ALTER TABLE dbo.Referral ADD ContactName NVARCHAR(255) NULL;

        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Referral') AND name = N'ReferredEmail')
        BEGIN
            IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Referral') AND name = N'ReferredToEmailAddress')
                EXEC sp_rename 'dbo.Referral.ReferredToEmailAddress', 'ReferredEmail', 'COLUMN';
            ELSE
                ALTER TABLE dbo.Referral ADD ReferredEmail NVARCHAR(255) NULL;
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Referral') AND name = N'ReferralCode')
            ALTER TABLE dbo.Referral ADD ReferralCode VARCHAR(64) NULL;

        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Referral') AND name = N'Status')
            ALTER TABLE dbo.Referral ADD Status NVARCHAR(50) NOT NULL CONSTRAINT DF_Referral_Status DEFAULT 'Queued';

        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Referral') AND name = N'EmailSentDate')
        BEGIN
            IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Referral') AND name = N'DateTimeReferred')
                EXEC sp_rename 'dbo.Referral.DateTimeReferred', 'EmailSentDate', 'COLUMN';
            ELSE
                ALTER TABLE dbo.Referral ADD EmailSentDate DATETIME2 NOT NULL CONSTRAINT DF_Referral_EmailSentDate DEFAULT SYSUTCDATETIME();
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Referral') AND name = N'ClaimedDate')
        BEGIN
            IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Referral') AND name = N'DateTimeAccepted')
                EXEC sp_rename 'dbo.Referral.DateTimeAccepted', 'ClaimedDate', 'COLUMN';
            ELSE
                ALTER TABLE dbo.Referral ADD ClaimedDate DATETIME2 NULL;
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Referral') AND name = N'CreditedDate')
        BEGIN
            IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Referral') AND name = N'DateTimeCredited')
                EXEC sp_rename 'dbo.Referral.DateTimeCredited', 'CreditedDate', 'COLUMN';
            ELSE IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Referral') AND name = N'DateTImeCreditted')
                EXEC sp_rename 'dbo.Referral.DateTImeCreditted', 'CreditedDate', 'COLUMN';
            ELSE
                ALTER TABLE dbo.Referral ADD CreditedDate DATETIME2 NULL;
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Referral') AND name = N'CreditAmount')
            ALTER TABLE dbo.Referral ADD CreditAmount DECIMAL(18,2) NULL;

        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Referral') AND name = N'CreatedByUserID')
        BEGIN
            IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Referral') AND name = N'ReferredByUserID')
                EXEC sp_rename 'dbo.Referral.ReferredByUserID', 'CreatedByUserID', 'COLUMN';
            ELSE
                ALTER TABLE dbo.Referral ADD CreatedByUserID INT NULL;
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Referral') AND name = N'ReferredOrgID')
        BEGIN
            IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Referral') AND name = N'AcceptingOrganizationID')
                EXEC sp_rename 'dbo.Referral.AcceptingOrganizationID', 'ReferredOrgID', 'COLUMN';
            ELSE IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Referral') AND name = N'RecipientOrgranizationID')
                EXEC sp_rename 'dbo.Referral.RecipientOrgranizationID', 'ReferredOrgID', 'COLUMN';
            ELSE
                ALTER TABLE dbo.Referral ADD ReferredOrgID INT NULL;
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Referral') AND name = N'ClaimedByUserID')
        BEGIN
            IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Referral') AND name = N'AcceptedByUserID')
                EXEC sp_rename 'dbo.Referral.AcceptedByUserID', 'ClaimedByUserID', 'COLUMN';
            ELSE IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Referral') AND name = N'RecipientUserID')
                EXEC sp_rename 'dbo.Referral.RecipientUserID', 'ClaimedByUserID', 'COLUMN';
            ELSE
                ALTER TABLE dbo.Referral ADD ClaimedByUserID INT NULL;
        END;

        -- ReferralCode is a bearer secret. A unique filtered index prevents an
        -- ambiguous invitation lookup even if a generator or import is faulty.
        IF EXISTS (
            SELECT 1
            FROM dbo.Referral
            WHERE ReferralCode IS NOT NULL
            GROUP BY ReferralCode
            HAVING COUNT(*) > 1
        )
            THROW 51412, 'Duplicate referral codes exist. Resolve them before deploying the referral program.', 1;

        IF EXISTS (
            SELECT 1
            FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'dbo.Referral')
              AND name = N'IX_Referral_ReferralCode'
              AND is_unique = 0
        )
            DROP INDEX IX_Referral_ReferralCode ON dbo.Referral;

        IF NOT EXISTS (
            SELECT 1
            FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'dbo.Referral')
              AND name = N'IX_Referral_ReferralCode'
        )
        BEGIN
            CREATE UNIQUE NONCLUSTERED INDEX IX_Referral_ReferralCode ON dbo.Referral (ReferralCode) WHERE ReferralCode IS NOT NULL;
        END;
    END;

    -- 2. Add ReferralCreditBalance to dbo.Organization if missing
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Organization') AND name = N'ReferralCreditBalance')
    BEGIN
        ALTER TABLE dbo.Organization ADD ReferralCreditBalance DECIMAL(18,2) NOT NULL CONSTRAINT DF_Organization_ReferralCreditBalance DEFAULT 0.00;
    END;

    -- 3. Add system template columns to dbo.SystemSettings if missing
    IF OBJECT_ID(N'dbo.SystemSettings', N'U') IS NOT NULL
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.SystemSettings') AND name = N'ReferralEmailPrefaceDefault')
            ALTER TABLE dbo.SystemSettings ADD ReferralEmailPrefaceDefault NVARCHAR(1000) NULL;

        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.SystemSettings') AND name = N'ReferralEmailBodyDefault')
            ALTER TABLE dbo.SystemSettings ADD ReferralEmailBodyDefault NVARCHAR(4000) NULL;

        UPDATE dbo.SystemSettings
        SET
            ReferralEmailPrefaceDefault = ISNULL(ReferralEmailPrefaceDefault, N'As we discussed recently, our nonprofit has been using Email Money App (EMA) and we''ve been really pleased with it, and I promised to send you more details.' + CHAR(13) + CHAR(10) + CHAR(13) + CHAR(10) + 'Please read more about it below,' + CHAR(13) + CHAR(10) + CHAR(13) + CHAR(10) + 'Thanks' + CHAR(13) + CHAR(10) + CHAR(13) + CHAR(10) + 'John'),
            ReferralEmailBodyDefault = ISNULL(ReferralEmailBodyDefault, N'Why we think Email Money App is worth a look:' + CHAR(13) + CHAR(10) + CHAR(13) + CHAR(10) + '•  EMA automatically records Interac e-Transfers and Pre-Authorized Debits (PADs) straight into your donation database.' + CHAR(13) + CHAR(10) + '•  $1.00 per e-Transfer or PAD, which is far lower than typical credit-card fees (2.2–2.9% + 30¢) or CanadaHelps at 4% or more.' + CHAR(13) + CHAR(10) + '•  One-month sandbox trial with absolutely no contact information required (no email, no phone)' + CHAR(13) + CHAR(10) + '•  Easy to start — import your own donor data or have EMA assist with the conversion for a small one-time fee' + CHAR(13) + CHAR(10) + '•  Built specifically for Canadian churches and non-profits' + CHAR(13) + CHAR(10) + CHAR(13) + CHAR(10) + 'If your organization becomes an EMA customer, both of our organizations will receive a one-month EMA credit, based on the smaller organization''s monthly EMA fee.' + CHAR(13) + CHAR(10) + CHAR(13) + CHAR(10) + 'PS – EMA will not use the name or email gathered for this message to market directly to you.')
        WHERE ReferralEmailPrefaceDefault IS NULL
           OR ReferralEmailBodyDefault IS NULL;
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
