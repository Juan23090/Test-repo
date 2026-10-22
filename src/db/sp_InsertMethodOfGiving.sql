CREATE OR ALTER PROCEDURE dbo.sp_InsertMethodOfGiving
    @EMAOrganizationID INT,
    @Description NVARCHAR(100),
    @IsActive CHAR(1),
    @CreatedByUserID INT
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.Organization
        WHERE EMAOrganizationID = @EMAOrganizationID
    )
    BEGIN
        THROW 52061, 'The organization does not exist.', 1;
    END;

    -- Assign the next per-org Sequence so new methods get a proper ordering slot (matches sp_InsertCampus/Fund).
    DECLARE @NextSequence SMALLINT;
    SELECT @NextSequence =
        CONVERT(SMALLINT, ISNULL(MAX([Sequence]), 0) + 1)
    FROM dbo.OrganizationMethodOfGiving
    WHERE EMAOrganizationID = @EMAOrganizationID;

    INSERT INTO dbo.OrganizationMethodOfGiving
    (
        EMAOrganizationId,
        ChMSMethodOfGivingID,
        [Sequence],
        Description,
        IsActive,
        CRAReceiptable,
        AcknowledgeToDonor,
        AcknowledgeToOrganization,
        AcknowledgeToEMASupport,
        DateTimeCreated,
        CreatedByUserId
    )
    VALUES
    (
        @EMAOrganizationID,
        '0',
        @NextSequence,
        @Description,
        @IsActive,
        'Y',
        'Y',
        'Y',
        'Y',
        SYSUTCDATETIME(),
        @CreatedByUserID
    );

    SELECT CONVERT(INT, SCOPE_IDENTITY()) AS EMAMethodOfGivingID;
END;
GO
