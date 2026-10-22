/*
    Seeds a dedicated organization used by the tenant-isolation tests in
    EMA.Api.Tests.Security.

    The tests are read-only: they assert that organization-scoped lookups
    return nothing when asked for another tenant's rows. Nothing here is
    mutated by a test run, so this only needs to be applied once per
    database.

    Records are found by name, never by hard-coded ID, so this is safe to
    run against a shared development database where identity values
    differ.

    Run against emasqldatabase-dev via the mssql extension.
*/

SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @OrganizationName VARCHAR(255) =
    'ZZ Tenant Isolation Test Org';

DECLARE @DonorName VARCHAR(255) =
    'ZZ Isolation Test Donor';

DECLARE @FundName VARCHAR(255) =
    'ZZ Isolation Test Fund';

DECLARE @CampusName VARCHAR(255) =
    'ZZ Isolation Test Campus';

------------------------------------------------------------------
-- Organization
------------------------------------------------------------------
IF NOT EXISTS
(
    SELECT 1
    FROM dbo.Organization
    WHERE OrganizationName = @OrganizationName
)
BEGIN
    INSERT INTO dbo.Organization
        (OrganizationName, DateTimeCreated, DateTimeUpdated)
    VALUES
        (@OrganizationName, SYSUTCDATETIME(), SYSUTCDATETIME());
END;

DECLARE @TestOrganizationID INT =
(
    SELECT TOP (1) EMAOrganizationID
    FROM dbo.Organization
    WHERE OrganizationName = @OrganizationName
);

------------------------------------------------------------------
-- Donor owned by the test organization
------------------------------------------------------------------
IF NOT EXISTS
(
    SELECT 1
    FROM dbo.Donor
    WHERE EMAOrganizationID = @TestOrganizationID
      AND OfficialDonorName = @DonorName
)
BEGIN
    INSERT INTO dbo.Donor
        (EMAOrganizationID, OfficialDonorName, EmailAddress,
         DateTimeCreated, CreatedByUserID,
         DateTimeUpdated, UpdatedbyUserID)
    VALUES
        (@TestOrganizationID, @DonorName,
         'zz-isolation-test@example.invalid',
         SYSUTCDATETIME(), 1, SYSUTCDATETIME(), 1);
END;

------------------------------------------------------------------
-- Fund and campus owned by the test organization
------------------------------------------------------------------
IF NOT EXISTS
(
    SELECT 1
    FROM dbo.OrganizationFund
    WHERE EMAOrganizationID = @TestOrganizationID
      AND Name = @FundName
)
BEGIN
    INSERT INTO dbo.OrganizationFund
        (EMAOrganizationID, Sequence, Name, IsActive,
         DateTimeCreated, CreatedByUserID)
    VALUES
        (@TestOrganizationID, 1, @FundName, 'Y',
         GETUTCDATE(), 1);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.OrganizationCampus
    WHERE EMAOrganizationID = @TestOrganizationID
      AND Name = @CampusName
)
BEGIN
    INSERT INTO dbo.OrganizationCampus
        (EMAOrganizationID, ChMSCampusID, Sequence, Name, IsActive,
         DateTimeCreated, CreatedByUserID)
    VALUES
        (@TestOrganizationID, 0, 1, @CampusName, 'Y',
         GETUTCDATE(), 1);
END;

------------------------------------------------------------------
-- Donation owned by the test organization
------------------------------------------------------------------
DECLARE @TestDonorID INT =
(
    SELECT TOP (1) EMADonorID
    FROM dbo.Donor
    WHERE EMAOrganizationID = @TestOrganizationID
      AND OfficialDonorName = @DonorName
);

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.Donation
    WHERE EMAOrganizationID = @TestOrganizationID
      AND Comments = 'ZZ Isolation Test Donation'
)
BEGIN
    INSERT INTO dbo.Donation
        (EMAOrganizationID, EMADonorID, Comments, DateTimeReceived,
         DonationAmount, IsTemplate,
         DateTimeCreated, CreatedbyUserID,
         DateTimeUpdated, UpdatedbyUserID)
    VALUES
        (@TestOrganizationID, @TestDonorID,
         'ZZ Isolation Test Donation', SYSUTCDATETIME(),
         1.00, 'N', SYSUTCDATETIME(), 1, SYSUTCDATETIME(), 1);
END;

COMMIT TRANSACTION;
GO

-- Confirm what the tests will look up.
SELECT
    o.EMAOrganizationID,
    o.OrganizationName,
    (SELECT COUNT(*) FROM dbo.Donor d
      WHERE d.EMAOrganizationID = o.EMAOrganizationID) AS Donors,
    (SELECT COUNT(*) FROM dbo.Donation dn
      WHERE dn.EMAOrganizationID = o.EMAOrganizationID) AS Donations
FROM dbo.Organization o
WHERE o.OrganizationName = 'ZZ Tenant Isolation Test Org';
GO
