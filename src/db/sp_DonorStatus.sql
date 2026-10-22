-- ============================================================================
-- Donor Status entity stored procedures.
-- Backed by DonorStatus. Run against emasqldatabase-dev via the mssql extension.
-- No sp_Delete: deactivated via IsActive (0), never hard-deleted.
--
-- DonorStatus shape (confirmed via INFORMATION_SCHEMA), differs from the other lookups:
--   EMAOrganizationId (int), DonorStatusID (smallint, NOT identity), Sequence (smallint),
--   Description (nchar), IsActive (bit). No audit columns.
-- DonorStatusID is assigned MAX+1 (not identity); Sequence is per-org MAX+1.
-- ============================================================================

CREATE OR ALTER PROCEDURE sp_InsertDonorStatus
    @EMAOrganizationID INT,
    @Description       NVARCHAR(255),
    @IsActive          BIT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @NextID  INT = ISNULL((SELECT MAX(DonorStatusID) FROM DonorStatus), 0) + 1;
    DECLARE @NextSeq INT =
        ISNULL((SELECT MAX(Sequence) FROM DonorStatus
                WHERE EMAOrganizationId = @EMAOrganizationID), 0) + 1;

    INSERT INTO DonorStatus
        (EMAOrganizationId, DonorStatusID, Sequence, Description, IsActive)
    VALUES
        (@EMAOrganizationID, @NextID, @NextSeq, @Description, @IsActive);

    SELECT @NextID AS DonorStatusID;
END
GO

CREATE OR ALTER PROCEDURE sp_UpdateDonorStatus
    @EMAOrganizationID INT,
    @DonorStatusID INT,
    @Description    NVARCHAR(255),
    @IsActive       BIT,
    @Sequence       INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE DonorStatus
    SET Description = @Description,
        IsActive    = @IsActive,
        Sequence    = ISNULL(@Sequence, Sequence)
    WHERE DonorStatusID = @DonorStatusID
      AND EMAOrganizationId = @EMAOrganizationID;
END
GO
