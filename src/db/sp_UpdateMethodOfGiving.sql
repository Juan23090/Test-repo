-- ============================================================================
-- Method of Giving update stored procedure.
-- Backed by OrganizationMethodOfGiving. Run against emasqldatabase-dev via the mssql extension.
--
-- Replaces the previous proc, which only updated Description and IsActive. Roger added the
-- CRAReceiptable / JournalEntryInDetail / AcknowledgeTo* switches (CHAR(1) 'Y'/'N') and the
-- AcknowledgementMessage (VARCHAR(512), nullable); this proc now persists all of them.
-- NOTE: the audit column is named "UpdatedbyUserID" (lowercase b, capital ID) — matches the table.
-- ============================================================================

CREATE OR ALTER PROCEDURE sp_UpdateMethodOfGiving
    @EMAOrganizationID         INT,
    @EMAMethodOfGivingID       INT,
    @Description               VARCHAR(MAX),
    @IsActive                  CHAR(1),
    @CRAReceiptable            CHAR(1),
    @JournalEntryInDetail      CHAR(1),
    @AcknowledgeToDonor        CHAR(1),
    @AcknowledgeToOrganization CHAR(1),
    @AcknowledgeToEMASupport   CHAR(1),
    @AcknowledgementMessage    VARCHAR(512),
    @UpdatedByUserID           INT,
    @Sequence INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE OrganizationMethodOfGiving
    SET Sequence = ISNULL(@Sequence, Sequence),
        Description               = @Description,
        IsActive                  = @IsActive,
        CRAReceiptable            = @CRAReceiptable,
        JournalEntryInDetail      = @JournalEntryInDetail,
        AcknowledgeToDonor        = @AcknowledgeToDonor,
        AcknowledgeToOrganization = @AcknowledgeToOrganization,
        AcknowledgeToEMASupport   = @AcknowledgeToEMASupport,
        AcknowledgementMessage    = @AcknowledgementMessage,
        DateTimeUpdated           = GETUTCDATE(),
        UpdatedbyUserID           = @UpdatedByUserID
    WHERE EMAMethodOfGivingID = @EMAMethodOfGivingID
      AND EMAOrganizationID = @EMAOrganizationID;
END
GO
