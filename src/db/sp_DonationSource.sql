-- ============================================================================
-- Donation Source entity stored procedures.
-- Backed by OrganizationDonationSource. Run against emasqldatabase-dev via the mssql extension.
-- No sp_Delete: deactivated via IsActive ('N'), never hard-deleted (Roger).
-- ChMSDonationSource is INT (default 0); EMADonationSourceID is IDENTITY.
-- ============================================================================

CREATE OR ALTER PROCEDURE sp_InsertDonationSource
    @EMAOrganizationID INT,
    @Description       VARCHAR(255),
    @IsActive          CHAR(1),
    @CreatedByUserID   INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @NextSeq INT =
        ISNULL((SELECT MAX(Sequence) FROM OrganizationDonationSource
                WHERE EMAOrganizationID = @EMAOrganizationID), 0) + 1;

    INSERT INTO OrganizationDonationSource
        (EMAOrganizationID, ChMSDonationSource, Sequence, Description, IsActive,
         DateTimeCreated, CreatedByUserId)
    VALUES
        (@EMAOrganizationID, 0, @NextSeq, @Description, @IsActive,
         GETUTCDATE(), @CreatedByUserID);

    SELECT CAST(SCOPE_IDENTITY() AS INT) AS EMADonationSourceID;
END
GO

CREATE OR ALTER PROCEDURE sp_UpdateDonationSource
    @EMAOrganizationID   INT,
    @EMADonationSourceID INT,
    @Description         VARCHAR(255),
    @IsActive            CHAR(1),
    @UpdatedByUserID     INT,
    @Sequence            INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE OrganizationDonationSource
    SET Sequence        = ISNULL(@Sequence, Sequence),
        Description     = @Description,
        IsActive        = @IsActive,
        DateTimeUpdated = GETUTCDATE(),
        UpdatedByUserId = @UpdatedByUserID
    WHERE EMADonationSourceID = @EMADonationSourceID
      AND EMAOrganizationID = @EMAOrganizationID;
END
GO
