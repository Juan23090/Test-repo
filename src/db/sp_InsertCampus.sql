CREATE OR ALTER PROCEDURE dbo.sp_InsertCampus
    @EMAOrganizationID INT,
    @CampusName NVARCHAR(100),
    @IsActive CHAR(1),
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
            THROW 52041, 'The organization does not exist.', 1;
        END;

        DECLARE @NextSequence SMALLINT;

        SELECT @NextSequence =
            CONVERT(SMALLINT, ISNULL(MAX([Sequence]), 0) + 1)
        FROM dbo.OrganizationCampus WITH (UPDLOCK, HOLDLOCK)
        WHERE EMAOrganizationID = @EMAOrganizationID;

        INSERT INTO dbo.OrganizationCampus
        (
            EMAOrganizationID,
            ChMSCampusID,
            [Sequence],
            Name,
            IsActive,
            DateTimeCreated,
            CreatedByUserId
        )
        VALUES
        (
            @EMAOrganizationID,
            '0',
            @NextSequence,
            @CampusName,
            @IsActive,
            SYSUTCDATETIME(),
            @CreatedByUserID
        );

        SELECT CONVERT(INT, SCOPE_IDENTITY()) AS EMACampusID;

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
