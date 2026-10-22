-- ============================================================================
-- InstallReferralProgramSchema.sql
-- Description: Schema (tables, columns, indexes) for the EMA Referral Program.
--
-- Run order:
--   1. InstallReferralProgramSchema.sql  <-- this file
--   2. 20260901_UpgradeReferralSchema.sql (renames for pre-existing installs)
--   3. 20260901_ReferralStoredProcedures.sql
--   4. 20260901_AddOrganizationReferralMenuItems.sql
--   5. 20260901_AddReferralRolePermissions.sql
--
-- Stored procedures are NOT defined here. dbo.sp_Referral_* live only in
-- 20260901_ReferralStoredProcedures.sql so there is one definition to maintain.
-- ============================================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- 1. Create dbo.Referral Table
IF OBJECT_ID('dbo.FK_Organization_Referral', 'F') IS NOT NULL
    ALTER TABLE dbo.Organization DROP CONSTRAINT FK_Organization_Referral;
GO

IF OBJECT_ID('dbo.Referral', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Referral (
        ReferralID BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY CLUSTERED,
        ReferringOrgID INT NOT NULL,
        ReferredOrgName NVARCHAR(255) NULL,
        ContactName NVARCHAR(255) NULL,
        ReferredEmail NVARCHAR(255) NOT NULL,
        ReferralCode VARCHAR(64) NULL,
        -- Queued -> Sent -> Accepted -> Credited. A row is written as Queued
        -- before the invitation is dispatched and promoted to Sent only once
        -- the email provider has accepted it.
        Status NVARCHAR(50) NOT NULL CONSTRAINT DF_Referral_Status DEFAULT 'Queued',
        EmailSentDate DATETIME2 NOT NULL CONSTRAINT DF_Referral_EmailSentDate DEFAULT SYSUTCDATETIME(),
        ClaimedDate DATETIME2 NULL,
        CreditedDate DATETIME2 NULL,
        CreditAmount DECIMAL(18,2) NULL,
        CreatedByUserID INT NULL,
        ReferredOrgID INT NULL,
        ClaimedByUserID INT NULL,
        CONSTRAINT FK_Referral_ReferringOrg FOREIGN KEY (ReferringOrgID) REFERENCES dbo.Organization(EMAOrganizationID)
    );

    CREATE NONCLUSTERED INDEX IX_Referral_ReferredEmail ON dbo.Referral (ReferredEmail) INCLUDE (Status);
    CREATE NONCLUSTERED INDEX IX_Referral_ReferringOrgID ON dbo.Referral (ReferringOrgID);
    CREATE NONCLUSTERED INDEX IX_Referral_ReferredOrgID ON dbo.Referral (ReferredOrgID) WHERE ReferredOrgID IS NOT NULL;
    CREATE UNIQUE NONCLUSTERED INDEX IX_Referral_ReferralCode ON dbo.Referral (ReferralCode) WHERE ReferralCode IS NOT NULL;
END;
GO

-- 2. Add ReferralCreditBalance to dbo.Organization
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Organization') AND name = N'ReferralCreditBalance')
BEGIN
    ALTER TABLE dbo.Organization ADD ReferralCreditBalance DECIMAL(18,2) NOT NULL CONSTRAINT DF_Organization_ReferralCreditBalance DEFAULT 0.00;
END;
GO

-- 3. Add the referral email template columns to dbo.SystemSettings.
--    These names are a contract with
--    EMA.Api.Services.Referrals.ReferralService.GetEligibilityAsync.
IF OBJECT_ID(N'dbo.SystemSettings', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.SystemSettings') AND name = N'ReferralEmailPrefaceDefault')
        ALTER TABLE dbo.SystemSettings ADD ReferralEmailPrefaceDefault NVARCHAR(1000) NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.SystemSettings') AND name = N'ReferralEmailBodyDefault')
        ALTER TABLE dbo.SystemSettings ADD ReferralEmailBodyDefault NVARCHAR(4000) NULL;
END;
GO
