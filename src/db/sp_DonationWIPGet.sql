CREATE OR ALTER PROCEDURE dbo.sp_DonationWIPGet
    @EMAOrganizationID INT,
    @EMADonationID BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT wip.DDDDJasonPayload
    FROM dbo.DonationWIP AS wip
    INNER JOIN dbo.Donation AS donation
        ON donation.EMADonationID = wip.EMADonationID
       AND donation.EMAOrganizationID = @EMAOrganizationID
    WHERE wip.EMADonationID = @EMADonationID;
END;
GO
