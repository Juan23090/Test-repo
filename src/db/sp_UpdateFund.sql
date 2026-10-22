CREATE OR ALTER PROCEDURE dbo.sp_UpdateFund
    @EMAOrganizationID INT,
    @EMAFundID INT,
    @Name VARCHAR(MAX),
    @IsActive CHAR(1),
    @TaxDeductible CHAR(1),
    @EMACampusID INT,
    @UpdatedByUserID INT,
    @Sequence INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @EMACampusID IS NOT NULL
       AND NOT EXISTS
       (
           SELECT 1
           FROM dbo.OrganizationCampus
           WHERE EMACampusID = @EMACampusID
             AND EMAOrganizationID = @EMAOrganizationID
       )
    BEGIN
        THROW 52081, 'The campus does not belong to the organization.', 1;
    END;

    UPDATE dbo.OrganizationFund
    SET
        Sequence = ISNULL(@Sequence, Sequence),
        Name = @Name,
        IsActive = @IsActive,
        TaxDeductible = @TaxDeductible,
        EMACampusId = @EMACampusID,
        DateTimeUpdated = SYSUTCDATETIME(),
        UpdatedbyUserId = @UpdatedByUserID
    WHERE EMAFundID = @EMAFundID
      AND EMAOrganizationID = @EMAOrganizationID;

    IF @@ROWCOUNT = 0
    BEGIN
        THROW 52082, 'The fund was not found in the organization.', 1;
    END;
END;
GO
