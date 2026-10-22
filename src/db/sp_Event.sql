-- ============================================================================
-- Events entity stored procedures (formerly Currency Types).
-- Backed by OrganizationEvent. Run against emasqldatabase-dev via the mssql extension.
-- No sp_DeleteEvent: events are deactivated via IsActive ('N'), never hard-deleted (Roger).
--
-- Identity layout (confirmed via sys.identity_columns):
--   * Sequence  IS the identity column  -> do NOT insert it; it auto-generates.
--   * EMAEventID is NOT identity         -> assign explicitly as MAX(EMAEventID)+1.
-- ChMSEventID defaults to '0'.
-- ============================================================================

CREATE OR ALTER PROCEDURE sp_InsertEvent
    @EMAOrganizationID INT,
    @Description       VARCHAR(255),
    @IsActive          CHAR(1),
    @CreatedByUserID   INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @NewID INT =
        ISNULL((SELECT MAX(EMAEventID) FROM OrganizationEvent), 0) + 1;

    INSERT INTO OrganizationEvent
        (EMAEventID, EMAOrganizationID, ChMSEventID, Description, IsActive,
         DateTimeCreated, CreatedByUserId)
    VALUES
        (@NewID, @EMAOrganizationID, '0', @Description, @IsActive,
         GETUTCDATE(), @CreatedByUserID);

    SELECT @NewID AS EMAEventID;
END
GO

CREATE OR ALTER PROCEDURE sp_UpdateEvent
    @EMAOrganizationID INT,
    @EMAEventID      INT,
    @Description     VARCHAR(255),
    @IsActive        CHAR(1),
    @UpdatedByUserID INT,
    @Sequence INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- NOTE: OrganizationEvent.Sequence is an IDENTITY column and cannot be updated, so Events
    -- are not reorderable. @Sequence is accepted (to match the API EXEC signature) but ignored.
    UPDATE OrganizationEvent
    SET Description     = @Description,
        IsActive        = @IsActive,
        DateTimeUpdated = GETUTCDATE(),
        UpdatedByUserId = @UpdatedByUserID
    WHERE EMAEventID = @EMAEventID
      AND EMAOrganizationID = @EMAOrganizationID;
END
GO
