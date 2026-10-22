CREATE OR ALTER PROCEDURE dbo.sp_MergeDonors
    @FromID INT,
    @ToID INT,
    @EMAOrganizationID INT,
    @UpdatedByUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @FromOrgID INT, @ToOrgID INT;
    SELECT @FromOrgID = EMAOrganizationID FROM dbo.Donor WHERE EMADonorID = @FromID;
    SELECT @ToOrgID = EMAOrganizationID FROM dbo.Donor WHERE EMADonorID = @ToID;

    IF @FromOrgID IS NULL
       OR @ToOrgID IS NULL
       OR @FromOrgID <> @ToOrgID
       OR @FromOrgID <> @EMAOrganizationID
    BEGIN
        RAISERROR ('Both donors must exist and belong to the same organization.', 16, 1);
        RETURN;
    END

    BEGIN TRANSACTION;
    BEGIN TRY
        UPDATE to_donor
        SET 
            ChMSDonorID = COALESCE(to_donor.ChMSDonorID, from_donor.ChMSDonorID),
            EMADefaultCampusID = COALESCE(to_donor.EMADefaultCampusID, from_donor.EMADefaultCampusID),
            EmailAddress = COALESCE(NULLIF(to_donor.EmailAddress, ''), NULLIF(from_donor.EmailAddress, '')),
            GivingNumber = COALESCE(NULLIF(to_donor.GivingNumber, ''), NULLIF(from_donor.GivingNumber, '')),
            InteracDonorName = COALESCE(NULLIF(to_donor.InteracDonorName, ''), NULLIF(from_donor.InteracDonorName, '')),
            OfficialDonorName = COALESCE(NULLIF(to_donor.OfficialDonorName, ''), NULLIF(from_donor.OfficialDonorName, '')),
            Address = COALESCE(NULLIF(to_donor.Address, ''), NULLIF(from_donor.Address, '')),
            City = COALESCE(NULLIF(to_donor.City, ''), NULLIF(from_donor.City, '')),
            ProvinceStateCode = COALESCE(to_donor.ProvinceStateCode, from_donor.ProvinceStateCode),
            PostalCode = COALESCE(NULLIF(to_donor.PostalCode, ''), NULLIF(from_donor.PostalCode, '')),
            Country = COALESCE(NULLIF(to_donor.Country, ''), NULLIF(from_donor.Country, '')),
            PhoneNumber = COALESCE(NULLIF(to_donor.PhoneNumber, ''), NULLIF(from_donor.PhoneNumber, '')),
            AcknowledgmentForManualTransactions = COALESCE(to_donor.AcknowledgmentForManualTransactions, from_donor.AcknowledgmentForManualTransactions),
            DonorStatusID = COALESCE(to_donor.DonorStatusID, from_donor.DonorStatusID),
            DateTimeUpdated = GETUTCDATE(),
            UpdatedbyUserID = @UpdatedByUserID
        FROM dbo.Donor to_donor
        INNER JOIN dbo.Donor from_donor
            ON from_donor.EMADonorID = @FromID
           AND from_donor.EMAOrganizationID =
               @EMAOrganizationID
        WHERE to_donor.EMADonorID = @ToID
          AND to_donor.EMAOrganizationID =
              @EMAOrganizationID;

        DELETE FROM dbo.OrganizationMailingListDonor
        WHERE EMADonorID = @FromID
          AND EMAOrganizationID = @EMAOrganizationID
          AND MailingListID IN (SELECT MailingListID FROM dbo.OrganizationMailingListDonor WHERE EMADonorID = @ToID AND EMAOrganizationID = @EMAOrganizationID);

        UPDATE dbo.OrganizationMailingListDonor
        SET EMADonorID = @ToID
        WHERE EMADonorID = @FromID
          AND EMAOrganizationID = @EMAOrganizationID;

        DELETE FROM dbo.OrganizationMailingListInstanceDonor
        WHERE EMADonorID = @FromID
          AND EMAOrganizationID = @EMAOrganizationID
          AND MailingListInstanceID IN (SELECT MailingListInstanceID FROM dbo.OrganizationMailingListInstanceDonor WHERE EMADonorID = @ToID AND EMAOrganizationID = @EMAOrganizationID);

        UPDATE dbo.OrganizationMailingListInstanceDonor
        SET EMADonorID = @ToID
        WHERE EMADonorID = @FromID
          AND EMAOrganizationID = @EMAOrganizationID;

        DELETE FROM dbo.OrganizationGroupDonor
        WHERE EMADonorID = @FromID
          AND EMAOrganizationID = @EMAOrganizationID
          AND OrganizationGroupID IN (SELECT OrganizationGroupID FROM dbo.OrganizationGroupDonor WHERE EMADonorID = @ToID AND EMAOrganizationID = @EMAOrganizationID);

        UPDATE dbo.OrganizationGroupDonor
        SET EMADonorID = @ToID
        WHERE EMADonorID = @FromID
          AND EMAOrganizationID = @EMAOrganizationID;

        UPDATE dbo.Donation
        SET EMADonorID = @ToID,
            DateTimeUpdated = GETUTCDATE(),
            UpdatedbyUserID = @UpdatedByUserID
        WHERE EMADonorID = @FromID
          AND EMAOrganizationID =
              @EMAOrganizationID;

        UPDATE dbo.DonationReceipt
        SET EMADonorID = @ToID
        WHERE EMADonorID = @FromID
          AND EMAOrganizationID =
              @EMAOrganizationID;

        UPDATE dbo.DonorJourney
        SET EMADonorId = @ToID
        WHERE EMADonorId = @FromID
          AND EMAOrganizationID =
              @EMAOrganizationID;

        UPDATE dbo.Organization
        SET EMADonorIDForAnonymous = @ToID,
            DateTimeUpdated = GETUTCDATE(),
            UpdatedByUserID = @UpdatedByUserID
        WHERE EMADonorIDForAnonymous = @FromID
          AND EMAOrganizationID =
              @EMAOrganizationID;

        DELETE FROM dbo.Donor
        WHERE EMADonorID = @FromID
          AND EMAOrganizationID =
              @EMAOrganizationID;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO
