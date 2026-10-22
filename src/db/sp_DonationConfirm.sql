CREATE OR ALTER PROCEDURE dbo.sp_DonationConfirm
    @EMAOrganizationID INT,
    @EMADonationID BIGINT,
    @UpdatedByUserID INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Donation
    SET
        DateTimeUpdated = SYSUTCDATETIME(),
        UpdatedbyUserID = @UpdatedByUserID
    WHERE EMADonationID = @EMADonationID
      AND EMAOrganizationID = @EMAOrganizationID;
END;
GO
