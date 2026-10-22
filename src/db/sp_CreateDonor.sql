CREATE OR ALTER PROCEDURE dbo.sp_CreateDonor
    @EMAOrganizationID INT,
    @EmailAddress VARCHAR(MAX),
    @InteracDonorName VARCHAR(MAX),
    @Name VARCHAR(MAX),
    @Address VARCHAR(MAX),
    @City VARCHAR(MAX),
    @ProvinceStateCode CHAR(2),
    @PostalCode VARCHAR(MAX),
    @Country VARCHAR(MAX),
    @PhoneNumber VARCHAR(MAX),
    @EMADefaultCampusID INT,
    @DonorStatusID INT,
    @IsDonorPilotingEtransfers CHAR(1) = 'N',
    @AcknowledgmentForManualTransactions CHAR(1) = 'Y',
    @AcknowledgementForAutomatedTransactions CHAR(1) = 'Y',
    @CreatedByUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @EMAOrganizationID <= 0
       OR NOT EXISTS
       (
           SELECT 1
           FROM dbo.Organization
           WHERE EMAOrganizationID = @EMAOrganizationID
       )
    BEGIN
        THROW 52001, 'The organization does not exist.', 1;
    END;

    IF @EMADefaultCampusID IS NOT NULL
       AND NOT EXISTS
       (
           SELECT 1
           FROM dbo.OrganizationCampus
           WHERE EMAOrganizationID = @EMAOrganizationID
             AND EMACampusID = @EMADefaultCampusID
       )
    BEGIN
        THROW 52002, 'The default campus does not belong to the organization.', 1;
    END;

    IF @DonorStatusID IS NOT NULL
       AND NOT EXISTS
       (
           SELECT 1
           FROM dbo.DonorStatus
           WHERE EMAOrganizationId = @EMAOrganizationID
             AND DonorStatusID = @DonorStatusID
       )
    BEGIN
        THROW 52003, 'The donor status does not belong to the organization.', 1;
    END;

    IF NULLIF(LTRIM(RTRIM(@Name)), '') IS NULL
       AND NULLIF(LTRIM(RTRIM(@InteracDonorName)), '') IS NULL
    BEGIN
        THROW 52004, 'At least one donor name is required.', 1;
    END;

    INSERT INTO dbo.Donor
    (
        EMAOrganizationID,
        EmailAddress,
        InteracDonorName,
        OfficialDonorName,
        Address,
        City,
        ProvinceStateCode,
        PostalCode,
        Country,
        PhoneNumber,
        EMADefaultCampusID,
        DonorStatusID,
        IsDonorPilotingEtransfers,
        AcknowledgmentForManualTransactions,
        AcknowledgementForAutomatedTransactions,
        DateTimeCreated,
        CreatedByUserID,
        DateTimeUpdated,
        UpdatedbyUserID
    )
    VALUES
    (
        @EMAOrganizationID,
        @EmailAddress,
        @InteracDonorName,
        @Name,
        @Address,
        @City,
        @ProvinceStateCode,
        @PostalCode,
        @Country,
        @PhoneNumber,
        @EMADefaultCampusID,
        @DonorStatusID,
        ISNULL(NULLIF(@IsDonorPilotingEtransfers, ''), 'N'),
        ISNULL(NULLIF(@AcknowledgmentForManualTransactions, ''), 'Y'),
        ISNULL(NULLIF(@AcknowledgementForAutomatedTransactions, ''), 'Y'),
        SYSUTCDATETIME(),
        @CreatedByUserID,
        SYSUTCDATETIME(),
        @CreatedByUserID
    );

    SELECT CONVERT(INT, SCOPE_IDENTITY()) AS EMADonorID;
END;
GO
