-- ============================================================================
-- Referral Program Stored Procedures
--
-- Run order:
--   1. InstallReferralProgramSchema.sql   (tables, columns, indexes, defaults)
--   2. 20260901_UpgradeReferralSchema.sql (renames for pre-existing installs)
--   3. 20260901_ReferralStoredProcedures.sql  <-- this file
--   4. 20260901_AddOrganizationReferralMenuItems.sql
--   5. 20260901_AddReferralRolePermissions.sql
--
-- This file is the ONLY definition of the sp_Referral_* procedures. The result
-- set of sp_Referral_GetHistory is a contract with
-- EMA.Api.Services.Referrals.ReferralService.GetReferralHistoryAsync and is
-- guarded by ReferralDatabaseContractTests. Do not add, remove or rename a
-- column here without updating both.
-- ============================================================================

CREATE OR ALTER PROCEDURE dbo.sp_Referral_CheckEligibility
    @EMAOrganizationID INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @DateCreated DATETIME2;
    DECLARE @DaysActive INT = 0;
    DECLARE @DonationCount INT = 0;
    DECLARE @IsEligible BIT = 0;
    DECLARE @Reason NVARCHAR(255) = '';

    IF NOT EXISTS (SELECT 1 FROM dbo.Organization WHERE EMAOrganizationID = @EMAOrganizationID)
    BEGIN
        SELECT
            CAST(0 AS BIT) AS IsEligible,
            'Organization not found.' AS Reason,
            0 AS DaysActive,
            0 AS DonationCount;
        RETURN;
    END;

    SELECT @DateCreated = DateTimeCreated
    FROM dbo.Organization
    WHERE EMAOrganizationID = @EMAOrganizationID;

    -- If DateTimeCreated is NULL on the organization record, fall back to the
    -- organization's earliest recorded activity / donation date.
    IF @DateCreated IS NULL
    BEGIN
        SELECT @DateCreated = MIN(COALESCE(DateTimeCreated, DateTimeReceived))
        FROM dbo.Donation
        WHERE EMAOrganizationID = @EMAOrganizationID;
    END;

    SET @DaysActive =
        CASE
            WHEN @DateCreated IS NULL THEN 0
            ELSE DATEDIFF(DAY, @DateCreated, SYSUTCDATETIME())
        END;

    SELECT @DonationCount = COUNT_BIG(1)
    FROM dbo.Donation
    WHERE EMAOrganizationID = @EMAOrganizationID;

    IF @DaysActive >= 14 AND @DonationCount > 0
    BEGIN
        SET @IsEligible = 1;
        SET @Reason = 'Organization is eligible to send referrals.';
    END
    ELSE IF @DaysActive < 14 AND @DonationCount = 0
    BEGIN
        SET @IsEligible = 0;
        SET @Reason = 'Organizations must be active for at least 14 days and have processed at least 1 donation.';
    END
    ELSE IF @DaysActive < 14
    BEGIN
        SET @IsEligible = 0;
        SET @Reason = 'Organizations must be active for at least 14 days before sending referrals.';
    END
    ELSE
    BEGIN
        SET @IsEligible = 0;
        SET @Reason = 'Organizations must have processed at least 1 donation before sending referrals.';
    END;

    SELECT
        @IsEligible AS IsEligible,
        @Reason AS Reason,
        @DaysActive AS DaysActive,
        @DonationCount AS DonationCount;
END;
GO

CREATE OR ALTER PROCEDURE dbo.sp_Referral_Accept
    @ReferralCode VARCHAR(64),
    @ReferredEmail NVARCHAR(255),
    @ReferredOrgID INT,
    @AcceptedByUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ReferralID BIGINT;
    DECLARE @ReferringOrgID INT;
    DECLARE @ReferringOrgName NVARCHAR(255);
    DECLARE @InvitedEmail NVARCHAR(255);
    DECLARE @CurrentStatus NVARCHAR(50);
    DECLARE @EmailSentDate DATETIME2;
    DECLARE @ReferredOrgCreatedDate DATETIME2;
    DECLARE @NormalizedCode VARCHAR(64) = LTRIM(RTRIM(@ReferralCode));
    DECLARE @NormalizedEmail NVARCHAR(255) = LTRIM(RTRIM(@ReferredEmail));

    IF @NormalizedCode IS NULL OR LEN(@NormalizedCode) = 0
    BEGIN
        SELECT
            'INVALID_CODE' AS Status,
            'Please provide a valid referral code.' AS Message,
            CAST(NULL AS BIGINT) AS ReferralID,
            CAST(NULL AS INT) AS ReferringOrgID,
            CAST(NULL AS NVARCHAR(255)) AS ReferringOrgName;
        RETURN;
    END;

    IF @NormalizedEmail IS NULL OR LEN(@NormalizedEmail) = 0
    BEGIN
        SELECT
            'INVALID_EMAIL' AS Status,
            'Please provide the invitation email address.' AS Message,
            CAST(NULL AS BIGINT) AS ReferralID,
            CAST(NULL AS INT) AS ReferringOrgID,
            CAST(NULL AS NVARCHAR(255)) AS ReferringOrgName;
        RETURN;
    END;

    -- 1. Check if the accepting organization was already credited or claimed
    IF EXISTS (
        SELECT 1
        FROM dbo.Referral
        WHERE ReferredOrgID = @ReferredOrgID AND Status IN ('Accepted', 'Credited')
    )
    BEGIN
        SELECT
            'ALREADY_CLAIMED' AS Status,
            'This organization has already claimed a referral invitation.' AS Message,
            CAST(NULL AS BIGINT) AS ReferralID,
            CAST(NULL AS INT) AS ReferringOrgID,
            CAST(NULL AS NVARCHAR(255)) AS ReferringOrgName;
        RETURN;
    END;

    -- 2. Resolve the specific invitation by its cryptographically random ReferralCode.
    SELECT
        @ReferralID = r.ReferralID,
        @ReferringOrgID = r.ReferringOrgID,
        @ReferringOrgName = o.OrganizationName,
        @InvitedEmail = r.ReferredEmail,
        @CurrentStatus = r.Status,
        @EmailSentDate = r.EmailSentDate
    FROM dbo.Referral r
    INNER JOIN dbo.Organization o ON o.EMAOrganizationID = r.ReferringOrgID
    WHERE r.ReferralCode = @NormalizedCode;

    IF @ReferralID IS NULL
    BEGIN
        SELECT
            'NOT_FOUND' AS Status,
            'No referral invitation was found matching this referral code.' AS Message,
            CAST(NULL AS BIGINT) AS ReferralID,
            CAST(NULL AS INT) AS ReferringOrgID,
            CAST(NULL AS NVARCHAR(255)) AS ReferringOrgName;
        RETURN;
    END;

    -- 3. Check status: single-use secret cannot be re-claimed once Accepted or Credited
    IF @CurrentStatus IN ('Accepted', 'Credited')
    BEGIN
        SELECT
            'ALREADY_CLAIMED' AS Status,
            'This referral invitation has already been claimed.' AS Message,
            @ReferralID AS ReferralID,
            @ReferringOrgID AS ReferringOrgID,
            @ReferringOrgName AS ReferringOrgName;
        RETURN;
    END;

    -- 4. Only invitations moved to 'Sent' are eligible for acceptance.
    --    Queued invitations (not yet delivered by email provider) are NOT claimable.
    IF @CurrentStatus <> 'Sent'
    BEGIN
        SELECT
            'NOT_ELIGIBLE' AS Status,
            'This referral invitation has not yet been delivered and cannot be claimed.' AS Message,
            @ReferralID AS ReferralID,
            @ReferringOrgID AS ReferringOrgID,
            @ReferringOrgName AS ReferringOrgName;
        RETURN;
    END;

    -- 5. Validate that the accepting user's email matches the invited email address
    IF @InvitedEmail IS NULL
       OR LOWER(LTRIM(RTRIM(@InvitedEmail))) <> LOWER(@NormalizedEmail)
    BEGIN
        SELECT
            'EMAIL_MISMATCH' AS Status,
            'The referral code does not match the invitation email address.' AS Message,
            @ReferralID AS ReferralID,
            @ReferringOrgID AS ReferringOrgID,
            @ReferringOrgName AS ReferringOrgName;
        RETURN;
    END;

    -- 6. Prevent self-referral
    IF @ReferringOrgID = @ReferredOrgID
    BEGIN
        SELECT
            'SELF_REFERRAL' AS Status,
            'An organization cannot claim a referral sent by itself.' AS Message,
            @ReferralID AS ReferralID,
            @ReferringOrgID AS ReferringOrgID,
            @ReferringOrgName AS ReferringOrgName;
        RETURN;
    END;

    -- 7. Anti-backdating rule: referral must have been sent before (or when) the org registered
    SELECT @ReferredOrgCreatedDate = DateTimeCreated
    FROM dbo.Organization
    WHERE EMAOrganizationID = @ReferredOrgID;

    IF @ReferredOrgCreatedDate IS NOT NULL AND @EmailSentDate > DATEADD(DAY, 1, @ReferredOrgCreatedDate)
    BEGIN
        SELECT
            'BACKDATED' AS Status,
            'Referral invitations must be sent prior to organization registration.' AS Message,
            @ReferralID AS ReferralID,
            @ReferringOrgID AS ReferringOrgID,
            @ReferringOrgName AS ReferringOrgName;
        RETURN;
    END;

    -- 8. Claim referral atomically. Status = 'Sent' ensures single-use execution.
    BEGIN TRANSACTION;

    UPDATE dbo.Referral
    SET
        ReferredOrgID = @ReferredOrgID,
        ClaimedByUserID = @AcceptedByUserID,
        Status = 'Accepted',
        ClaimedDate = SYSUTCDATETIME()
    WHERE ReferralID = @ReferralID
      AND Status = 'Sent';

    IF @@ROWCOUNT = 0
    BEGIN
        COMMIT TRANSACTION;

        SELECT
            'ALREADY_CLAIMED' AS Status,
            'This referral invitation has already been claimed.' AS Message,
            @ReferralID AS ReferralID,
            @ReferringOrgID AS ReferringOrgID,
            @ReferringOrgName AS ReferringOrgName;
        RETURN;
    END;

    COMMIT TRANSACTION;

    SELECT
        'SUCCESS' AS Status,
        'Referral successfully claimed!' AS Message,
        @ReferralID AS ReferralID,
        @ReferringOrgID AS ReferringOrgID,
        @ReferringOrgName AS ReferringOrgName;
END;
GO

CREATE OR ALTER PROCEDURE dbo.sp_Referral_GetHistory
    @EMAOrganizationID INT
AS
BEGIN
    SET NOCOUNT ON;

    /*
        Returns every referral this organization sent or received.

        IsReferrer distinguishes the two directions so the caller can count
        "sent" without inflating it with referrals the organization received.

        ReferringAmount and RecipientAmount are reserved for the billing credit
        run, which is not yet implemented. They are typed NULL placeholders so
        the result-set contract stays stable when crediting lands.
    */
    SELECT
        r.ReferralID,
        CASE WHEN r.ReferringOrgID = @EMAOrganizationID THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS IsReferrer,
        r.EmailSentDate AS DateReferred,
        refOrg.OrganizationName AS ReferringOrganization,
        ISNULL(sentByUser.UserName, '') AS ReferredByUser,
        ISNULL(r.ContactName, '') AS ReferredTo,
        r.ReferredEmail AS EmailAddress,
        ISNULL(accOrg.OrganizationName, ISNULL(r.ReferredOrgName, '')) AS RecipientOrganization,
        ISNULL(claimedByUser.UserName, '') AS RecipientUser,
        r.ClaimedDate AS DateAccepted,
        r.CreditedDate AS DateCredited,
        CAST(NULL AS DECIMAL(18,2)) AS ReferringAmount,
        CAST(NULL AS DECIMAL(18,2)) AS RecipientAmount,
        r.CreditAmount,
        r.Status
    FROM dbo.Referral r
    INNER JOIN dbo.Organization refOrg ON refOrg.EMAOrganizationID = r.ReferringOrgID
    LEFT JOIN dbo.Organization accOrg ON accOrg.EMAOrganizationID = r.ReferredOrgID
    LEFT JOIN dbo.[User] sentByUser ON sentByUser.UserID = r.CreatedByUserID
    LEFT JOIN dbo.[User] claimedByUser ON claimedByUser.UserID = r.ClaimedByUserID
    WHERE r.ReferringOrgID = @EMAOrganizationID
       OR r.ReferredOrgID = @EMAOrganizationID
    ORDER BY r.EmailSentDate DESC;
END;
GO
