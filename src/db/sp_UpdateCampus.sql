CREATE OR ALTER PROCEDURE dbo.sp_UpdateCampus
    @EMAOrganizationID INT,
    @EMACampusID INT,
    @Name VARCHAR(MAX),
    @IsActive CHAR(1),
    @UpdatedByUserID INT,
    @Sequence INT = NULL,
    @DefaultCampusFundID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.OrganizationCampus
    SET
        Sequence = ISNULL(@Sequence, Sequence),
        Name = @Name,
        IsActive = @IsActive,
        -- Direct assignment (not ISNULL-keep): callers always send the current
        -- value, and passing NULL clears the campus's default fund on purpose.
        DefaultCampusFundID = @DefaultCampusFundID,
        DateTimeUpdated = SYSUTCDATETIME(),
        UpdatedbyUserId = @UpdatedByUserID
    WHERE EMACampusID = @EMACampusID
      AND EMAOrganizationID = @EMAOrganizationID;

    IF @@ROWCOUNT = 0
    BEGIN
        THROW 52071, 'The campus was not found in the organization.', 1;
    END;
END;
GO
