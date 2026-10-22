CREATE OR ALTER PROCEDURE dbo.sp_EmailSave
    @EMAOrganizationID INT,
    @EMADonationID BIGINT,
    @Comments VARCHAR(MAX),
    @Tribute VARCHAR(MAX),
    @TributeType VARCHAR(1),
    @OfficialDonorName VARCHAR(MAX),
    @Address VARCHAR(MAX),
    @City VARCHAR(MAX),
    @ProvinceStateCode VARCHAR(MAX),
    @PostalCode VARCHAR(MAX),
    @PhoneNumber VARCHAR(MAX),
    @UpdatedByUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @TributeType IS NOT NULL
       AND @TributeType NOT IN ('H', 'M')
    BEGIN
        THROW 52031, 'The tribute type is invalid.', 1;
    END;

    BEGIN TRANSACTION;
    BEGIN TRY
        DECLARE @EMADonorID INT;

        SELECT @EMADonorID = EMADonorID
        FROM dbo.Donation WITH (UPDLOCK, HOLDLOCK)
        WHERE EMADonationID = @EMADonationID
          AND EMAOrganizationID = @EMAOrganizationID;

        IF @EMADonorID IS NULL
        BEGIN
            THROW 52032, 'The donation was not found in the organization.', 1;
        END;

        UPDATE dbo.Donor
        SET
            OfficialDonorName = @OfficialDonorName,
            Address = @Address,
            City = @City,
            ProvinceStateCode = @ProvinceStateCode,
            PostalCode = @PostalCode,
            PhoneNumber = @PhoneNumber,
            DateTimeUpdated = SYSUTCDATETIME(),
            UpdatedbyUserID = @UpdatedByUserID
        WHERE EMADonorID = @EMADonorID
          AND EMAOrganizationID = @EMAOrganizationID;

        IF @@ROWCOUNT = 0
        BEGIN
            THROW 52033, 'The donation donor was not found in the organization.', 1;
        END;

        UPDATE dbo.Donation
        SET
            Comments = @Comments,
            Tribute = @Tribute,
            TributeType = @TributeType,
            zzzDonorToOrgChangeRequest = 'Y',
            DateTimeUpdated = SYSUTCDATETIME(),
            UpdatedbyUserID = @UpdatedByUserID
        WHERE EMADonationID = @EMADonationID
          AND EMAOrganizationID = @EMAOrganizationID;

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
