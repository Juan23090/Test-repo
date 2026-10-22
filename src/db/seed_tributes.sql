/*
    DEVELOPMENT-ONLY SAMPLE DATA

    This script fails closed until a developer deliberately sets all three
    variables below. Never enable it against a shared, test, or production
    database.
*/
DECLARE @AllowDevelopmentSeed BIT = 0;
DECLARE @OrganizationID INT = NULL;
DECLARE @ActorUserID INT = NULL;

IF @AllowDevelopmentSeed <> 1
   OR @OrganizationID IS NULL
   OR @OrganizationID <= 0
   OR @ActorUserID IS NULL
   OR @ActorUserID <= 0
BEGIN
    THROW 51090,
        'Development tribute seed is disabled. Set the explicit development variables before running it.',
        1;
END;

SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- 1. Insert tribute types for the selected development organization.
IF NOT EXISTS (SELECT 1 FROM OrganizationTributeType WHERE EMAOrganizationID = @OrganizationID AND EMATributeTypeID = 1)
BEGIN
    INSERT INTO OrganizationTributeType (EMAOrganizationID, EMATributeTypeID, Description, IsActive, Sequence)
    VALUES (@OrganizationID, 1, 'Honorarium', 'Y', 1);
END;

IF NOT EXISTS (SELECT 1 FROM OrganizationTributeType WHERE EMAOrganizationID = @OrganizationID AND EMATributeTypeID = 2)
BEGIN
    INSERT INTO OrganizationTributeType (EMAOrganizationID, EMATributeTypeID, Description, IsActive, Sequence)
    VALUES (@OrganizationID, 2, 'Memorial', 'Y', 2);
END;

-- 2. Insert a sample donor for the selected development organization.
IF NOT EXISTS (SELECT 1 FROM Donor WHERE EMAOrganizationID = @OrganizationID AND ChMSDonorID = '999')
BEGIN
    INSERT INTO Donor (EMAOrganizationID, ChMSDonorID, EmailAddress, OfficialDonorName, DateTimeCreated, CreatedByUserID, DateTimeUpdated, UpdatedbyUserID)
    VALUES (@OrganizationID, '999', 'testdonor@example.com', 'Test Donor', GETUTCDATE(), @ActorUserID, GETUTCDATE(), @ActorUserID);
END;

DECLARE @DonorID INT = (SELECT TOP 1 EMADonorID FROM Donor WHERE EMAOrganizationID = @OrganizationID AND ChMSDonorID = '999');

-- 3. Insert sample tribute donations.
INSERT INTO Donation (EMAOrganizationID, EMADonorID, Comments, DateTimeReceived, EMATributeTypeID, TributeText, DonationAmount, IsTemplate, DateTimeCreated, CreatedbyUserID, DateTimeUpdated, UpdatedbyUserID)
VALUES
(@OrganizationID, @DonorID, 'First Tribute Donation', '2026-06-01 10:00:00', 1, 'Jane Doe', 100.00, 'N', GETUTCDATE(), @ActorUserID, GETUTCDATE(), @ActorUserID),
(@OrganizationID, @DonorID, 'Second Tribute Donation', '2026-06-01 11:30:00', 2, 'John Smith', 250.00, 'N', GETUTCDATE(), @ActorUserID, GETUTCDATE(), @ActorUserID),
(@OrganizationID, @DonorID, 'Third Tribute Donation', '2026-06-02 09:15:00', 2, 'Peder Pedersen', 50.00, 'N', GETUTCDATE(), @ActorUserID, GETUTCDATE(), @ActorUserID),
(@OrganizationID, @DonorID, 'Fourth Tribute Donation (Duplicate to test grouping)', '2026-06-03 14:00:00', 2, 'John Smith', 75.00, 'N', GETUTCDATE(), @ActorUserID, GETUTCDATE(), @ActorUserID);

COMMIT TRANSACTION;
GO
