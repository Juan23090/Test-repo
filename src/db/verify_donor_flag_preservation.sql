/*
    Verification for canonical donor-flag preservation.

    An update caller that omits a flag, or sends an empty value, must leave
    each stored donor preference unchanged. Explicit values must still apply.

    Runs entirely inside a transaction and rolls back, so it changes nothing.
    Reads its own test donor from org 20 - no donor ID is hardcoded.

    Expected: every Result column below reads PASS.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

DECLARE @DonorID INT;
DECLARE @OrgID INT = 20;
DECLARE @UserID INT = 13;
DECLARE @Observed CHAR(1);

SELECT TOP (1)
    @DonorID = EMADonorID
FROM dbo.Donor
WHERE EMAOrganizationID = @OrgID
ORDER BY EMADonorID;

IF @DonorID IS NULL
BEGIN
    RAISERROR('No donor found in org %d to test against.', 16, 1, @OrgID);
    ROLLBACK TRANSACTION;
    RETURN;
END;

UPDATE dbo.Donor
SET
    IsDonorPilotingEtransfers = 'Y',
    AcknowledgmentForManualTransactions = 'N',
    AcknowledgementForAutomatedTransactions = 'Y'
WHERE EMADonorID = @DonorID;

EXEC dbo.sp_UpdateDonor
    @EMADonorID = @DonorID,
    @EMAOrganizationID = @OrgID,
    @EmailAddress = 'preservation-test@example.com',
    @InteracDonorName = NULL,
    @Name = 'Preservation Test',
    @Address = '1 Test St',
    @City = 'Testville',
    @ProvinceStateCode = 'ON',
    @PostalCode = 'A1A1A1',
    @Country = 'Canada',
    @PhoneNumber = '9999999999',
    @EMADefaultCampusID = NULL,
    @DonorStatusID = NULL,
    @UpdatedByUserID = @UserID;

SELECT @Observed = IsDonorPilotingEtransfers FROM dbo.Donor WHERE EMADonorID = @DonorID;
SELECT 'Omitted pilot parameter' AS Scenario, 'Y' AS Expected, @Observed AS Actual,
       CASE WHEN @Observed = 'Y' THEN 'PASS' ELSE 'FAIL' END AS Result;

SELECT @Observed = AcknowledgmentForManualTransactions FROM dbo.Donor WHERE EMADonorID = @DonorID;
SELECT 'Omitted manual acknowledgement parameter' AS Scenario, 'N' AS Expected, @Observed AS Actual,
       CASE WHEN @Observed = 'N' THEN 'PASS' ELSE 'FAIL' END AS Result;

SELECT @Observed = AcknowledgementForAutomatedTransactions FROM dbo.Donor WHERE EMADonorID = @DonorID;
SELECT 'Omitted automated acknowledgement parameter' AS Scenario, 'Y' AS Expected, @Observed AS Actual,
       CASE WHEN @Observed = 'Y' THEN 'PASS' ELSE 'FAIL' END AS Result;

EXEC dbo.sp_UpdateDonor
    @EMADonorID = @DonorID,
    @EMAOrganizationID = @OrgID,
    @EmailAddress = 'preservation-test@example.com',
    @InteracDonorName = NULL,
    @Name = 'Preservation Test',
    @Address = '1 Test St',
    @City = 'Testville',
    @ProvinceStateCode = 'ON',
    @PostalCode = 'A1A1A1',
    @Country = 'Canada',
    @PhoneNumber = '9999999999',
    @EMADefaultCampusID = NULL,
    @DonorStatusID = NULL,
    @IsDonorPilotingEtransfers = '',
    @AcknowledgmentForManualTransactions = '',
    @AcknowledgementForAutomatedTransactions = '',
    @UpdatedByUserID = @UserID;

IF EXISTS
(
    SELECT 1 FROM dbo.Donor
    WHERE EMADonorID = @DonorID
      AND IsDonorPilotingEtransfers = 'Y'
      AND AcknowledgmentForManualTransactions = 'N'
      AND AcknowledgementForAutomatedTransactions = 'Y'
)
    SELECT 'Empty values preserve all flags' AS Scenario, 'PASS' AS Result;
ELSE
    SELECT 'Empty values preserve all flags' AS Scenario, 'FAIL' AS Result;

EXEC dbo.sp_UpdateDonor
    @EMADonorID = @DonorID,
    @EMAOrganizationID = @OrgID,
    @EmailAddress = 'preservation-test@example.com',
    @InteracDonorName = NULL,
    @Name = 'Preservation Test',
    @Address = '1 Test St',
    @City = 'Testville',
    @ProvinceStateCode = 'ON',
    @PostalCode = 'A1A1A1',
    @Country = 'Canada',
    @PhoneNumber = '9999999999',
    @EMADefaultCampusID = NULL,
    @DonorStatusID = NULL,
    @IsDonorPilotingEtransfers = 'N',
    @AcknowledgmentForManualTransactions = 'Y',
    @AcknowledgementForAutomatedTransactions = 'N',
    @UpdatedByUserID = @UserID;

IF EXISTS
(
    SELECT 1 FROM dbo.Donor
    WHERE EMADonorID = @DonorID
      AND IsDonorPilotingEtransfers = 'N'
      AND AcknowledgmentForManualTransactions = 'Y'
      AND AcknowledgementForAutomatedTransactions = 'N'
)
    SELECT 'Explicit values update all flags' AS Scenario, 'PASS' AS Result;
ELSE
    SELECT 'Explicit values update all flags' AS Scenario, 'FAIL' AS Result;

ROLLBACK TRANSACTION;
PRINT 'Rolled back - no donor data was changed.';
