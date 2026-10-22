-- ============================================================================
-- Interaction Type entity stored procedures.
-- Backed by OrganizationInteractionType. Run against emasqldatabase-dev via the mssql extension.
-- No sp_Delete: deactivated via IsActive ('N'), never hard-deleted (Roger).
-- EMAInteractionTypeID is IDENTITY.
-- Requires the OrganizationInteractionType table to exist first - see DDL handed to Roger.
-- ============================================================================

CREATE OR ALTER PROCEDURE sp_InsertInteractionType
    @EMAOrganizationID INT,
    @Description       VARCHAR(255),
    @IsActive          CHAR(1),
    @CreatedByUserID   INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @NextSeq INT =
        ISNULL((SELECT MAX(Sequence) FROM OrganizationInteractionType
                WHERE EMAOrganizationID = @EMAOrganizationID), 0) + 1;

    INSERT INTO OrganizationInteractionType
        (EMAOrganizationID, Sequence, Description, IsActive,
         DateTimeCreated, CreatedByUserID)
    VALUES
        (@EMAOrganizationID, @NextSeq, @Description, @IsActive,
         GETUTCDATE(), @CreatedByUserID);

    SELECT CAST(SCOPE_IDENTITY() AS INT) AS EMAInteractionTypeID;
END
GO

CREATE OR ALTER PROCEDURE sp_UpdateInteractionType
    @EMAOrganizationID    INT,
    @EMAInteractionTypeID INT,
    @Description           VARCHAR(255),
    @IsActive              CHAR(1),
    @UpdatedByUserID        INT,
    @Sequence INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE OrganizationInteractionType
    SET Sequence = ISNULL(@Sequence, Sequence),
        Description     = @Description,
        IsActive        = @IsActive,
        DateTimeUpdated = GETUTCDATE(),
        UpdatedByUserID = @UpdatedByUserID
    WHERE EMAInteractionTypeID = @EMAInteractionTypeID
      AND EMAOrganizationID = @EMAOrganizationID;
END
GO
