-- ============================================================================
-- Tribute Type entity stored procedures.
-- Backed by OrganizationTributeType. Run against emasqldatabase-dev via the mssql extension.
-- No sp_Delete: deactivated via IsActive ('N'), never hard-deleted (Roger).
--
-- NOTE: OrganizationTributeType has NO audit columns. The @CreatedByUserID/@UpdatedByUserID
-- params are kept so the EXEC signatures match the API functions, but are not written.
-- EMATributeTypeID IS an identity column (confirmed via sys.columns) -> do NOT insert it
-- explicitly; let it auto-generate and return SCOPE_IDENTITY(). Sequence is MAX()+1 per org.
-- ChMSTributeTypeID is varchar, defaults to '0'.
-- ============================================================================

CREATE OR ALTER PROCEDURE sp_InsertTributeType
    @EMAOrganizationID INT,
    @Description       VARCHAR(255),
    @IsActive          CHAR(1),
    @CreatedByUserID   INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @NextSeq INT =
        ISNULL((SELECT MAX(Sequence) FROM OrganizationTributeType
                WHERE EMAOrganizationID = @EMAOrganizationID), 0) + 1;

    INSERT INTO OrganizationTributeType
        (EMAOrganizationID, ChMSTributeTypeID, Sequence, Description, IsActive)
    VALUES
        (@EMAOrganizationID, '0', @NextSeq, @Description, @IsActive);

    SELECT CAST(SCOPE_IDENTITY() AS INT) AS EMATributeTypeID;
END
GO

CREATE OR ALTER PROCEDURE sp_UpdateTributeType
    @EMAOrganizationID INT,
    @EMATributeTypeID INT,
    @Description      VARCHAR(255),
    @IsActive         CHAR(1),
    @UpdatedByUserID  INT,
    @Sequence INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE OrganizationTributeType
    SET Sequence = ISNULL(@Sequence, Sequence),
        Description = @Description,
        IsActive    = @IsActive
    WHERE EMATributeTypeID = @EMATributeTypeID
      AND EMAOrganizationID = @EMAOrganizationID;
END
GO
