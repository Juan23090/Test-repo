CREATE OR ALTER PROCEDURE dbo.sp_DonationSave
    @EMAOrganizationID INT,
    @EMADonationID BIGINT,
    @JsonPayload NVARCHAR(MAX),
    @UpdatedByUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF ISJSON(@JsonPayload) <> 1
    BEGIN
        THROW 52101, 'The donation payload is not valid JSON.', 1;
    END;

    DECLARE @PayloadDonationID BIGINT =
        TRY_CONVERT(
            BIGINT,
            JSON_VALUE(@JsonPayload, '$.Donation.EMADonationID')
        );

    DECLARE @EMADonorID INT =
        TRY_CONVERT(
            INT,
            JSON_VALUE(@JsonPayload, '$.Donation.EMADonorID')
        );

    DECLARE @DonorPayloadID INT =
        TRY_CONVERT(
            INT,
            JSON_VALUE(@JsonPayload, '$.Donor.EMADonorID')
        );

    DECLARE @OfficialDonorName NVARCHAR(200) =
        JSON_VALUE(@JsonPayload, '$.Donor.OfficialDonorName');

    DECLARE @InteracDonorName NVARCHAR(200) =
        JSON_VALUE(@JsonPayload, '$.Donor.InteracDonorName');

    DECLARE @Address NVARCHAR(200) =
        JSON_VALUE(@JsonPayload, '$.Donor.Address');

    DECLARE @City NVARCHAR(100) =
        JSON_VALUE(@JsonPayload, '$.Donor.City');

    DECLARE @ProvinceStateCode NVARCHAR(10) =
        JSON_VALUE(@JsonPayload, '$.Donor.ProvinceStateCode');

    DECLARE @PostalCode NVARCHAR(20) =
        JSON_VALUE(@JsonPayload, '$.Donor.PostalCode');

    DECLARE @DateTimeReceived DATETIME2(7) =
        TRY_CONVERT(
            DATETIME2(7),
            JSON_VALUE(@JsonPayload, '$.Donation.DateTimeReceived')
        );

    DECLARE @DonationAmount DECIMAL(19, 4) =
        TRY_CONVERT(
            DECIMAL(19, 4),
            JSON_VALUE(@JsonPayload, '$.Donation.DonationAmount')
        );

    DECLARE @EMACurrencyTypeID INT =
        TRY_CONVERT(
            INT,
            JSON_VALUE(@JsonPayload, '$.Donation.EMACurrencyTypeID')
        );

    DECLARE @EMAMethodOfGivingID INT =
        TRY_CONVERT(
            INT,
            JSON_VALUE(@JsonPayload, '$.Donation.EMAMethodOfGivingID')
        );

    DECLARE @ReferenceNumber NVARCHAR(100) =
        JSON_VALUE(@JsonPayload, '$.Donation.ReferenceNumber');

    DECLARE @Comments NVARCHAR(MAX) =
        JSON_VALUE(@JsonPayload, '$.Donation.Comments');

    DECLARE @Tribute NVARCHAR(200) =
        JSON_VALUE(@JsonPayload, '$.Donation.Tribute');

    DECLARE @TributeTypeValue NVARCHAR(50) =
        JSON_VALUE(@JsonPayload, '$.Donation.TributeType');

    DECLARE @TributeType VARCHAR(1) =
        CASE @TributeTypeValue
            WHEN 'Honoration' THEN 'H'
            WHEN 'Memorial' THEN 'M'
            WHEN 'H' THEN 'H'
            WHEN 'M' THEN 'M'
            ELSE NULL
        END;

    DECLARE @IsTemplateValue NVARCHAR(10) =
        JSON_VALUE(@JsonPayload, '$.Donation.IsTemplate');

    DECLARE @IsTemplate CHAR(1) =
        CASE
            WHEN @IsTemplateValue IN ('Y', '1', 'true', 'True')
                THEN 'Y'
            ELSE 'N'
        END;

    IF @PayloadDonationID IS NOT NULL
       AND @PayloadDonationID <> @EMADonationID
    BEGIN
        THROW 52102, 'The payload donation ID does not match the requested donation.', 1;
    END;

    IF @EMADonorID IS NULL
       OR @EMADonorID <= 0
       OR @DonorPayloadID IS NULL
       OR @DonorPayloadID <> @EMADonorID
    BEGIN
        THROW 52103, 'The payload donor is invalid.', 1;
    END;

    IF @DateTimeReceived IS NULL
    BEGIN
        THROW 52104, 'A valid donation received date is required.', 1;
    END;

    IF @DonationAmount IS NULL
       OR @DonationAmount <= 0
    BEGIN
        THROW 52105, 'The donation amount must be greater than zero.', 1;
    END;

    IF @EMAMethodOfGivingID IS NULL
       OR @EMAMethodOfGivingID <= 0
    BEGIN
        THROW 52106, 'A method of giving is required.', 1;
    END;

    DECLARE @Distributions TABLE
    (
        EMACampusID INT NULL,
        EMAFundID INT NULL,
        DistributionAmount DECIMAL(19, 4) NULL
    );

    INSERT INTO @Distributions
    (
        EMACampusID,
        EMAFundID,
        DistributionAmount
    )
    SELECT
        TRY_CONVERT(
            INT,
            JSON_VALUE(distribution.[value], '$.EMACampusID')
        ),
        TRY_CONVERT(
            INT,
            JSON_VALUE(distribution.[value], '$.EMAFundID')
        ),
        TRY_CONVERT(
            DECIMAL(19, 4),
            JSON_VALUE(distribution.[value], '$.DistributionAmount')
        )
    FROM OPENJSON(
             @JsonPayload,
             '$.Distributions'
         ) AS distribution;

    IF EXISTS
    (
        SELECT 1
        FROM @Distributions
        WHERE DistributionAmount < 0
    )
    BEGIN
        THROW 52107, 'Distribution amounts cannot be negative.', 1;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM @Distributions
        WHERE DistributionAmount IS NULL
          AND
          (
              ISNULL(EMACampusID, 0) <> 0
              OR ISNULL(EMAFundID, 0) <> 0
          )
    )
    BEGIN
        THROW 52108, 'A distribution amount is invalid.', 1;
    END;

    DELETE FROM @Distributions
    WHERE ISNULL(DistributionAmount, 0) = 0;

    IF NOT EXISTS
    (
        SELECT 1
        FROM @Distributions
    )
    BEGIN
        THROW 52109, 'At least one positive distribution is required.', 1;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM @Distributions
        WHERE ISNULL(EMACampusID, 0) <= 0
           OR ISNULL(EMAFundID, 0) <= 0
    )
    BEGIN
        THROW 52110, 'Every distribution requires a campus and fund.', 1;
    END;

    IF
    (
        SELECT SUM(DistributionAmount)
        FROM @Distributions
    ) <> @DonationAmount
    BEGIN
        THROW 52111, 'The distribution total must equal the donation amount.', 1;
    END;

    BEGIN TRANSACTION;
    BEGIN TRY
        IF NOT EXISTS
        (
            SELECT 1
            FROM dbo.Donation WITH (UPDLOCK, HOLDLOCK)
            WHERE EMADonationID = @EMADonationID
              AND EMAOrganizationID = @EMAOrganizationID
        )
        BEGIN
            THROW 52112, 'The donation was not found in the organization.', 1;
        END;

        IF NOT EXISTS
        (
            SELECT 1
            FROM dbo.Donor
            WHERE EMADonorID = @EMADonorID
              AND EMAOrganizationID = @EMAOrganizationID
        )
        BEGIN
            THROW 52113, 'The donor does not belong to the organization.', 1;
        END;

        IF NOT EXISTS
        (
            SELECT 1
            FROM dbo.OrganizationMethodOfGiving
            WHERE EMAMethodOfGivingID = @EMAMethodOfGivingID
              AND EMAOrganizationID = @EMAOrganizationID
        )
        BEGIN
            THROW 52114, 'The method of giving does not belong to the organization.', 1;
        END;

        IF @EMACurrencyTypeID IS NOT NULL
           AND NOT EXISTS
           (
               SELECT 1
               FROM dbo.OrganizationCurrencyType
               WHERE EMACurrencyTypeID = @EMACurrencyTypeID
                 AND EMAOrganizationID = @EMAOrganizationID
           )
        BEGIN
            THROW 52115, 'The currency type does not belong to the organization.', 1;
        END;

        IF EXISTS
        (
            SELECT 1
            FROM @Distributions AS distribution
            LEFT JOIN dbo.OrganizationCampus AS campus
                ON campus.EMACampusID =
                   distribution.EMACampusID
               AND campus.EMAOrganizationID =
                   @EMAOrganizationID
            WHERE campus.EMACampusID IS NULL
        )
        BEGIN
            THROW 52116, 'A distribution campus does not belong to the organization.', 1;
        END;

        IF EXISTS
        (
            SELECT 1
            FROM @Distributions AS distribution
            LEFT JOIN dbo.OrganizationFund AS fund
                ON fund.EMAFundID =
                   distribution.EMAFundID
               AND fund.EMAOrganizationID =
                   @EMAOrganizationID
            WHERE fund.EMAFundID IS NULL
        )
        BEGIN
            THROW 52117, 'A distribution fund does not belong to the organization.', 1;
        END;

        UPDATE dbo.Donor
        SET
            OfficialDonorName = @OfficialDonorName,
            InteracDonorName = @InteracDonorName,
            Address = @Address,
            City = @City,
            ProvinceStateCode = @ProvinceStateCode,
            PostalCode = @PostalCode,
            DateTimeUpdated = SYSUTCDATETIME(),
            UpdatedbyUserID = @UpdatedByUserID
        WHERE EMADonorID = @EMADonorID
          AND EMAOrganizationID = @EMAOrganizationID;

        UPDATE dbo.Donation
        SET
            EMADonorID = @EMADonorID,
            DateTimeReceived = @DateTimeReceived,
            DonationAmount = @DonationAmount,
            EMACurrencyTypeID = @EMACurrencyTypeID,
            EMAMethodOfGivingID = @EMAMethodOfGivingID,
            ReferenceNumber = @ReferenceNumber,
            Comments = @Comments,
            Tribute = @Tribute,
            TributeType = @TributeType,
            IsTemplate = @IsTemplate,
            DateTimeUpdated = SYSUTCDATETIME(),
            UpdatedbyUserID = @UpdatedByUserID
        WHERE EMADonationID = @EMADonationID
          AND EMAOrganizationID = @EMAOrganizationID;

        DELETE distribution
        FROM dbo.DonationDistribution AS distribution
        WHERE distribution.EMADonationID = @EMADonationID;

        INSERT INTO dbo.DonationDistribution
        (
            EMADonationID,
            EMACampusID,
            EMAFundID,
            DistributionAmount
        )
        SELECT
            @EMADonationID,
            EMACampusID,
            EMAFundID,
            DistributionAmount
        FROM @Distributions;

        DELETE FROM dbo.DonationWIP
        WHERE EMADonationID = @EMADonationID;

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
