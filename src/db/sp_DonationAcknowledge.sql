CREATE OR ALTER PROCEDURE dbo.sp_DonationAcknowledge
    @EMAOrganizationID INT,
    @EMADonationID BIGINT,
    @UpdatedByUserID INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Donation
    SET
        zzzDonorToOrgChangeRequest = 'N',
        DateTimeUpdated = SYSUTCDATETIME(),
        UpdatedbyUserID = @UpdatedByUserID
    WHERE EMADonationID = @EMADonationID
      AND EMAOrganizationID = @EMAOrganizationID;
END;
GO
