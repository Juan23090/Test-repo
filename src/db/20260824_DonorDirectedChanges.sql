SET XACT_ABORT ON;
GO

IF COL_LENGTH(
       'dbo.Organization',
       'MessageAboutAnonymousDonations') IS NULL
BEGIN
    ALTER TABLE dbo.Organization
        ADD MessageAboutAnonymousDonations NVARCHAR(2000) NULL;
END;
GO

IF COL_LENGTH(
       'dbo.Donation',
       'DonorToOrgChangeRequest') IS NULL
BEGIN
    ALTER TABLE dbo.Donation
        ADD DonorToOrgChangeRequest CHAR(1) NOT NULL
            CONSTRAINT DF_Donation_DonorToOrgChangeRequest
            DEFAULT ('N') WITH VALUES;
END;
GO

UPDATE dbo.DonationStep
SET NotifyDonor = 'Y'
WHERE DonationStepID = 85
  AND ISNULL(NotifyDonor, 'N') <> 'Y';
GO

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.DonationStep
    WHERE DonationStepID = 105
)
BEGIN
    INSERT INTO dbo.DonationStep
    (
        DonationStepID,
        Description,
        NotifyDonor,
        NotifyOrgAdmin,
        NotifyEMASupport,
        Explanation
    )
    VALUES
    (
        105,
        'Acknowledgement Editor Loaded',
        'N',
        'N',
        'N',
        'The donor opened the secure donation-change page from the acknowledgement email.'
    );
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.DonationStep
    WHERE DonationStepID = 110
)
BEGIN
    INSERT INTO dbo.DonationStep
    (
        DonationStepID,
        Description,
        NotifyDonor,
        NotifyOrgAdmin,
        NotifyEMASupport,
        Explanation
    )
    VALUES
    (
        110,
        'Acknowledgement Editor Saved',
        'N',
        'N',
        'N',
        'The donor saved changes from the secure donation-change page.'
    );
END;
GO

CREATE OR ALTER PROCEDURE dbo.sp_DonorDirectedChangesSave
    @EMAOrganizationID INT,
    @EMADonationID BIGINT,
    @TokenDonorID INT,
    @OfficialDonorName NVARCHAR(510) = NULL,
    @Address NVARCHAR(510) = NULL,
    @City NVARCHAR(255) = NULL,
    @ProvinceStateCode VARCHAR(32) = NULL,
    @PostalCode VARCHAR(32) = NULL,
    @Country NVARCHAR(255) = NULL,
    @PhoneNumber VARCHAR(64) = NULL,
    @Comments NVARCHAR(MAX) = NULL,
    @EMATributeTypeID INT = NULL,
    @TributeText NVARCHAR(MAX) = NULL,
    @IsTemplate CHAR(1),
    @MakeAnonymous BIT,
    @DistributionsJson NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @EMAOrganizationID <= 0 OR
       @EMADonationID <= 0 OR
       @TokenDonorID <= 0
    BEGIN
        THROW 52100, 'The donation-change identity is invalid.', 1;
    END;

    IF @IsTemplate NOT IN ('Y', 'N')
    BEGIN
        THROW 52101, 'The default-allocation value is invalid.', 1;
    END;

    IF ISJSON(@DistributionsJson) <> 1 OR
       LEFT(LTRIM(@DistributionsJson), 1) <> '['
    BEGIN
        THROW 52102, 'The fund allocations are not valid JSON.', 1;
    END;

    DECLARE @Distributions TABLE
    (
        EMACampusID INT NULL,
        EMAFundID INT NULL,
        DistributionAmount DECIMAL(19, 4) NOT NULL
    );

    INSERT INTO @Distributions
    (
        EMACampusID,
        EMAFundID,
        DistributionAmount
    )
    SELECT
        EMACampusID,
        EMAFundID,
        DistributionAmount
    FROM OPENJSON(@DistributionsJson)
    WITH
    (
        EMACampusID INT '$.EMACampusID',
        EMAFundID INT '$.EMAFundID',
        DistributionAmount DECIMAL(19, 4) '$.DistributionAmount'
    );

    IF NOT EXISTS
    (
        SELECT 1
        FROM @Distributions
    )
    BEGIN
        THROW 52103, 'At least one fund allocation is required.', 1;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM @Distributions
        WHERE DistributionAmount <= 0
           OR DistributionAmount > 99999999.99
    )
    BEGIN
        THROW 52104, 'Each fund allocation must contain a valid positive amount.', 1;
    END;

    IF EXISTS
    (
        SELECT
            EMACampusID,
            EMAFundID
        FROM @Distributions
        GROUP BY
            EMACampusID,
            EMAFundID
        HAVING COUNT(*) > 1
    )
    BEGIN
        THROW 52105, 'Duplicate fund allocations are not allowed.', 1;
    END;

    BEGIN TRANSACTION;

    BEGIN TRY
        DECLARE @UseCampuses CHAR(1);
        DECLARE @UseFunds CHAR(1);
        DECLARE @UseTributeTypes CHAR(1);
        DECLARE @UseAnonymous CHAR(1);
        DECLARE @AnonymousDonorID INT;

        SELECT
            @UseCampuses = UseCampuses,
            @UseFunds = UseFunds,
            @UseTributeTypes = UseTributeTypes,
            @UseAnonymous = UseAnonymous,
            @AnonymousDonorID = EMADonorIDForAnonymous
        FROM dbo.Organization WITH (UPDLOCK, HOLDLOCK)
        WHERE EMAOrganizationID = @EMAOrganizationID;

        IF @@ROWCOUNT = 0
        BEGIN
            THROW 52106, 'The organization was not found.', 1;
        END;

        DECLARE @CurrentDonorID INT;
        DECLARE @DonationAmount DECIMAL(19, 4);

        SELECT
            @CurrentDonorID = EMADonorID,
            @DonationAmount = DonationAmount
        FROM dbo.Donation WITH (UPDLOCK, HOLDLOCK)
        WHERE EMADonationID = @EMADonationID
          AND EMAOrganizationID = @EMAOrganizationID;

        IF @CurrentDonorID IS NULL
        BEGIN
            THROW 52107, 'The donation was not found.', 1;
        END;

        DECLARE @AlreadyAnonymous BIT =
            CASE
                WHEN @AnonymousDonorID IS NOT NULL AND
                     @CurrentDonorID = @AnonymousDonorID
                    THEN 1
                ELSE 0
            END;

        IF @CurrentDonorID <> @TokenDonorID AND
           @AlreadyAnonymous = 0
        BEGIN
            THROW 52108, 'This donation no longer belongs to the donor from the acknowledgement email.', 1;
        END;

        IF @AlreadyAnonymous = 1
        BEGIN
            SET @MakeAnonymous = 1;
        END;

        IF @MakeAnonymous = 1
        BEGIN
            IF @UseAnonymous <> 'Y'
            BEGIN
                THROW 52109, 'Anonymous donations are not enabled for this organization.', 1;
            END;

            IF @AnonymousDonorID IS NULL OR
               NOT EXISTS
               (
                   SELECT 1
                   FROM dbo.Donor
                   WHERE EMADonorID = @AnonymousDonorID
                     AND EMAOrganizationID = @EMAOrganizationID
               )
            BEGIN
                THROW 52110, 'The organization anonymous donor is not configured.', 1;
            END;
        END;

        IF @UseTributeTypes <> 'Y'
        BEGIN
            SET @EMATributeTypeID = NULL;
            SET @TributeText = NULL;
        END;
        ELSE IF @EMATributeTypeID IS NULL
        BEGIN
            SET @TributeText = NULL;
        END;
        ELSE IF NOT EXISTS
        (
            SELECT 1
            FROM dbo.OrganizationTributeType
            WHERE EMATributeTypeID = @EMATributeTypeID
              AND EMAOrganizationID = @EMAOrganizationID
              AND IsActive = 'Y'
        )
        BEGIN
            THROW 52111, 'The selected tribute type is not available.', 1;
        END;

        IF @UseCampuses = 'Y' AND
           EXISTS
           (
               SELECT 1
               FROM @Distributions
               WHERE EMACampusID IS NULL OR
                     EMACampusID <= 0
           )
        BEGIN
            THROW 52112, 'A campus is required for every fund allocation.', 1;
        END;

        IF @UseCampuses <> 'Y'
        BEGIN
            UPDATE @Distributions
            SET EMACampusID = NULL;
        END;

        IF @UseFunds = 'Y' AND
           EXISTS
           (
               SELECT 1
               FROM @Distributions
               WHERE EMAFundID IS NULL OR
                     EMAFundID <= 0
           )
        BEGIN
            THROW 52113, 'A fund is required for every fund allocation.', 1;
        END;

        IF @UseFunds <> 'Y'
        BEGIN
            UPDATE @Distributions
            SET EMAFundID = NULL;
        END;

        IF EXISTS
        (
            SELECT 1
            FROM @Distributions AS distribution
            LEFT JOIN dbo.OrganizationCampus AS campus
                ON campus.EMACampusID = distribution.EMACampusID
               AND campus.EMAOrganizationID = @EMAOrganizationID
            WHERE distribution.EMACampusID IS NOT NULL
              AND campus.EMACampusID IS NULL
        )
        BEGIN
            THROW 52114, 'A selected campus does not belong to this organization.', 1;
        END;

        IF EXISTS
        (
            SELECT 1
            FROM @Distributions AS distribution
            LEFT JOIN dbo.OrganizationFund AS fund
                ON fund.EMAFundID = distribution.EMAFundID
               AND fund.EMAOrganizationID = @EMAOrganizationID
            WHERE distribution.EMAFundID IS NOT NULL
              AND fund.EMAFundID IS NULL
        )
        BEGIN
            THROW 52115, 'A selected fund does not belong to this organization.', 1;
        END;

        DECLARE @DistributionTotal DECIMAL(19, 4);

        SELECT
            @DistributionTotal =
                SUM(DistributionAmount)
        FROM @Distributions;

        IF ROUND(@DistributionTotal, 2) <>
           ROUND(@DonationAmount, 2)
        BEGIN
            THROW 52116, 'The fund allocations must add up to the donation total.', 1;
        END;

        DECLARE @AuditPayload NVARCHAR(MAX);

        SELECT
            @AuditPayload =
            (
                SELECT
                    'DonorDirectedChanges' AS EventType,
                    JSON_QUERY
                    (
                        (
                            SELECT
                                donor.EMADonorID,
                                donor.OfficialDonorName,
                                donor.InteracDonorName,
                                donor.EmailAddress,
                                donor.Address,
                                donor.City,
                                donor.ProvinceStateCode,
                                donor.PostalCode,
                                donor.Country,
                                donor.PhoneNumber
                            FROM dbo.Donor AS donor
                            WHERE donor.EMADonorID = @CurrentDonorID
                              AND donor.EMAOrganizationID = @EMAOrganizationID
                            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
                        )
                    ) AS Donor,
                    JSON_QUERY
                    (
                        (
                            SELECT
                                donation.EMADonationID,
                                donation.EMADonorID,
                                donation.ReferenceNumber,
                                donation.DateTimeReceived,
                                donation.Comments,
                                donation.TributeText,
                                donation.EMATributeTypeID,
                                donation.DonationAmount,
                                donation.IsTemplate,
                                donation.DateTimeUpdated,
                                donation.UpdatedbyUserID
                            FROM dbo.Donation AS donation
                            WHERE donation.EMADonationID = @EMADonationID
                              AND donation.EMAOrganizationID = @EMAOrganizationID
                            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
                        )
                    ) AS Donation,
                    JSON_QUERY
                    (
                        (
                            SELECT
                                distribution.EMACampusID,
                                distribution.EMAFundID,
                                distribution.DistributionAmount
                            FROM dbo.DonationDistribution AS distribution
                            WHERE distribution.EMADonationID = @EMADonationID
                            ORDER BY distribution.EMADonationDistributionID
                            FOR JSON PATH
                        )
                    ) AS Distributions
                FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
            );

        INSERT INTO dbo.DonationAudit
        (
            EMADonationID,
            DateTimeCreated,
            UpdatedByUserID,
            DDDDJsonPayload
        )
        VALUES
        (
            @EMADonationID,
            SYSUTCDATETIME(),
            NULL,
            @AuditPayload
        );

        IF @MakeAnonymous = 0
        BEGIN
            UPDATE dbo.Donor
            SET
                OfficialDonorName = @OfficialDonorName,
                Address = @Address,
                City = @City,
                ProvinceStateCode = @ProvinceStateCode,
                PostalCode = @PostalCode,
                Country = @Country,
                PhoneNumber = @PhoneNumber,
                DateTimeUpdated = SYSUTCDATETIME()
            WHERE EMADonorID = @CurrentDonorID
              AND EMAOrganizationID = @EMAOrganizationID;
        END;

        UPDATE dbo.Donation
        SET
            EMADonorID =
                CASE
                    WHEN @MakeAnonymous = 1
                        THEN @AnonymousDonorID
                    ELSE EMADonorID
                END,
            InteracDonorName =
                CASE
                    WHEN @MakeAnonymous = 1
                        THEN NULL
                    ELSE InteracDonorName
                END,
            Comments = @Comments,
            TributeText = @TributeText,
            EMATributeTypeID = @EMATributeTypeID,
            IsTemplate = @IsTemplate,
            DonorToOrgChangeRequest = 'Y',
            DateTimeUpdated = SYSUTCDATETIME()
        WHERE EMADonationID = @EMADonationID
          AND EMAOrganizationID = @EMAOrganizationID;

        DELETE FROM dbo.DonationDistribution
        WHERE EMADonationID = @EMADonationID;

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

        IF EXISTS
        (
            SELECT 1
            FROM dbo.DonationStep
            WHERE DonationStepID = 110
        )
        AND NOT EXISTS
        (
            SELECT 1
            FROM dbo.DonationStepLog WITH (UPDLOCK, HOLDLOCK)
            WHERE EMADonationID = @EMADonationID
              AND DonationStepID = 110
        )
        BEGIN
            INSERT INTO dbo.DonationStepLog
            (
                EMADonationID,
                DonationStepID,
                DateTimeLogged
            )
            VALUES
            (
                @EMADonationID,
                110,
                SYSUTCDATETIME()
            );
        END;

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
