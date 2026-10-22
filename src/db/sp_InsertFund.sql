CREATE OR ALTER PROCEDURE dbo.sp_InsertFund
    @EMAOrganizationID INT,
    @FundName NVARCHAR(100),
    @IsActive CHAR(1),
    @TaxDeductible CHAR(1),
    @CreatedByUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;
    BEGIN TRY
        IF NOT EXISTS
        (
            SELECT 1
            FROM dbo.Organization WITH (UPDLOCK, HOLDLOCK)
            WHERE EMAOrganizationID = @EMAOrganizationID
        )
        BEGIN
            THROW 52051, 'The organization does not exist.', 1;
        END;

        DECLARE @NextSequence SMALLINT;

        SELECT @NextSequence =
            CONVERT(SMALLINT, ISNULL(MAX([Sequence]), 0) + 1)
        FROM dbo.OrganizationFund WITH (UPDLOCK, HOLDLOCK)
        WHERE EMAOrganizationID = @EMAOrganizationID;

        INSERT INTO dbo.OrganizationFund
        (
            EMAOrganizationID,
            [Sequence],
            Name,
            IsActive,
            TaxDeductible,
            DateTimeCreated,
            CreatedByUserId
        )
        VALUES
        (
            @EMAOrganizationID,
            @NextSequence,
            @FundName,
            @IsActive,
            @TaxDeductible,
            SYSUTCDATETIME(),
            @CreatedByUserID
        );

        SELECT CONVERT(INT, SCOPE_IDENTITY()) AS EMAFundID;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0
        BEGIN
            ROLLBACK TRANSACTION;
        END;

        THROW;
    END CATCH;
END;
GO
