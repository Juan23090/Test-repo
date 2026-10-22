CREATE OR ALTER PROCEDURE dbo.sp_MergeDonationSources
    @FromID INT,
    @ToID INT,
    @EMAOrganizationID INT,
    @UpdatedByUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @FromOrgID INT, @ToOrgID INT;
    SELECT @FromOrgID = EMAOrganizationID FROM dbo.OrganizationDonationSource WHERE EMADonationSourceID = @FromID;
    SELECT @ToOrgID = EMAOrganizationID FROM dbo.OrganizationDonationSource WHERE EMADonationSourceID = @ToID;

    IF @FromOrgID IS NULL
       OR @ToOrgID IS NULL
       OR @FromOrgID <> @ToOrgID
       OR @FromOrgID <> @EMAOrganizationID
    BEGIN
        RAISERROR ('Both donation sources must exist and belong to the same organization.', 16, 1);
        RETURN;
    END

    BEGIN TRANSACTION;
    BEGIN TRY
        UPDATE to_source
        SET 
            ChMSDonationSource = COALESCE(to_source.ChMSDonationSource, from_source.ChMSDonationSource),
            DateTimeUpdated = GETUTCDATE(),
            UpdatedByUserID = @UpdatedByUserID
        FROM dbo.OrganizationDonationSource to_source
        INNER JOIN dbo.OrganizationDonationSource from_source
            ON from_source.EMADonationSourceID = @FromID
           AND from_source.EMAOrganizationID =
               @EMAOrganizationID
        WHERE to_source.EMADonationSourceID = @ToID
          AND to_source.EMAOrganizationID =
              @EMAOrganizationID;

        UPDATE dbo.Donation
        SET EMADonationSourceID = @ToID,
            DateTimeUpdated = GETUTCDATE(),
            UpdatedbyUserID = @UpdatedByUserID
        WHERE EMADonationSourceID = @FromID
          AND EMAOrganizationID =
              @EMAOrganizationID;

        UPDATE dbo.Organization
        SET EMASourceIDForEtransfers = CASE WHEN EMASourceIDForEtransfers = @FromID THEN @ToID ELSE EMASourceIDForEtransfers END,
            EMASourceIDForPADs = CASE WHEN EMASourceIDForPADs = @FromID THEN @ToID ELSE EMASourceIDForPADs END,
            DateTimeUpdated = GETUTCDATE(),
            UpdatedByUserID = @UpdatedByUserID
        WHERE (EMASourceIDForEtransfers = @FromID OR EMASourceIDForPADs = @FromID)
          AND EMAOrganizationID =
              @EMAOrganizationID;

        DELETE FROM dbo.OrganizationDonationSource
        WHERE EMADonationSourceID = @FromID
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
