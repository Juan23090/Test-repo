CREATE OR ALTER PROCEDURE dbo.sp_MergeGivingMethods
    @FromID INT,
    @ToID INT,
    @EMAOrganizationID INT,
    @UpdatedByUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @FromOrgID INT, @ToOrgID INT;
    SELECT @FromOrgID = EMAOrganizationID FROM dbo.OrganizationMethodOfGiving WHERE EMAMethodOfGivingID = @FromID;
    SELECT @ToOrgID = EMAOrganizationID FROM dbo.OrganizationMethodOfGiving WHERE EMAMethodOfGivingID = @ToID;

    IF @FromOrgID IS NULL
       OR @ToOrgID IS NULL
       OR @FromOrgID <> @ToOrgID
       OR @FromOrgID <> @EMAOrganizationID
    BEGIN
        RAISERROR ('Both giving methods must exist and belong to the same organization.', 16, 1);
        RETURN;
    END

    BEGIN TRANSACTION;
    BEGIN TRY
        UPDATE to_method
        SET 
            GLAccountNumber = COALESCE(NULLIF(to_method.GLAccountNumber, ''), NULLIF(from_method.GLAccountNumber, '')),
            InvoiceGroup = COALESCE(to_method.InvoiceGroup, from_method.InvoiceGroup),
            AcknowledgeToDonor = COALESCE(to_method.AcknowledgeToDonor, from_method.AcknowledgeToDonor),
            AcknowledgeToOrganization = COALESCE(to_method.AcknowledgeToOrganization, from_method.AcknowledgeToOrganization),
            AcknowledgeToEMASupport = COALESCE(to_method.AcknowledgeToEMASupport, from_method.AcknowledgeToEMASupport),
            AcknowledgementMessage = COALESCE(NULLIF(to_method.AcknowledgementMessage, ''), NULLIF(from_method.AcknowledgementMessage, '')),
            JournalEntryInDetail = COALESCE(to_method.JournalEntryInDetail, from_method.JournalEntryInDetail),
            DateTimeUpdated = GETUTCDATE(),
            UpdatedbyUserID = @UpdatedByUserID
        FROM dbo.OrganizationMethodOfGiving to_method
        INNER JOIN dbo.OrganizationMethodOfGiving from_method
            ON from_method.EMAMethodOfGivingID = @FromID
           AND from_method.EMAOrganizationID =
               @EMAOrganizationID
        WHERE to_method.EMAMethodOfGivingID = @ToID
          AND to_method.EMAOrganizationID =
              @EMAOrganizationID;

        UPDATE dbo.Donation
        SET EMAMethodOfGivingID = @ToID,
            DateTimeUpdated = GETUTCDATE(),
            UpdatedbyUserID = @UpdatedByUserID
        WHERE EMAMethodOfGivingID = @FromID
          AND EMAOrganizationID =
              @EMAOrganizationID;

        UPDATE dbo.Organization
        SET EMAMethodOfGivingIDForETransfers = CASE WHEN EMAMethodOfGivingIDForETransfers = @FromID THEN @ToID ELSE EMAMethodOfGivingIDForETransfers END,
            EMAMethodOfGivingIDForPADs = CASE WHEN EMAMethodOfGivingIDForPADs = @FromID THEN @ToID ELSE EMAMethodOfGivingIDForPADs END,
            EMAMethodOfGivingIDForCash = CASE WHEN EMAMethodOfGivingIDForCash = @FromID THEN @ToID ELSE EMAMethodOfGivingIDForCash END,
            EMAMethodOfGivingIDForCheques = CASE WHEN EMAMethodOfGivingIDForCheques = @FromID THEN @ToID ELSE EMAMethodOfGivingIDForCheques END,
            DateTimeUpdated = GETUTCDATE(),
            UpdatedByUserID = @UpdatedByUserID
        WHERE (EMAMethodOfGivingIDForETransfers = @FromID
            OR EMAMethodOfGivingIDForPADs = @FromID
            OR EMAMethodOfGivingIDForCash = @FromID
            OR EMAMethodOfGivingIDForCheques = @FromID)
          AND EMAOrganizationID =
              @EMAOrganizationID;

        DELETE FROM dbo.OrganizationMethodOfGiving
        WHERE EMAMethodOfGivingID = @FromID
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
