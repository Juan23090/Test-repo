CREATE OR ALTER PROCEDURE dbo.sp_ProcessBankDonationDistributions
    @EMADonationID BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @OrgID INT,
            @DonorID INT,
            @DonationAmount MONEY,
            @Comments VARCHAR(MAX),
            @MethodOfGivingID INT,
            @CampusID INT,
            @OrgDefaultFundID INT,
            @PriorDonationID BIGINT,
            @PriorDonationAmount MONEY,
            @PriorIsTemplate CHAR(1),
            @PriorSplitCount INT;

    SELECT 
        @OrgID = EMAOrganizationID,
        @DonorID = EMADonorID,
        @DonationAmount = DonationAmount,
        @Comments = ISNULL(Comments, ''),
        @MethodOfGivingID = EMAMethodOfGivingID
    FROM dbo.Donation
    WHERE EMADonationID = @EMADonationID;

    IF @OrgID IS NULL OR @DonationAmount IS NULL OR @DonationAmount <= 0
    BEGIN
        RETURN;
    END

    SELECT 
        @OrgDefaultFundID = ISNULL(EMAFundIDForPADs, EMAFundIDForEtransfer),
        @CampusID = EMACampusIDForPADs
    FROM dbo.Organization
    WHERE EMAOrganizationID = @OrgID;

    -- Status 82: Donation Created from Bank Statement Import
    INSERT INTO dbo.DonationStepLog (
        EMADonationID,
        DonationStepID,
        DateTimeLogged
    )
    VALUES (
        @EMADonationID,
        82,
        SYSUTCDATETIME()
    );

    -- BRANCH 1: Manual Override
    IF @Comments LIKE 'Fund changed from %'
    BEGIN
        DECLARE @NewFundName VARCHAR(255),
                @OverriddenFundID INT;

        SELECT @NewFundName = LTRIM(RTRIM(SUBSTRING(
            @Comments, 
            CHARINDEX(' to ', @Comments) + 4, 
            CHARINDEX(' during bank statement processing', @Comments) - (CHARINDEX(' to ', @Comments) + 4)
        )));

        IF @NewFundName IS NOT NULL AND LEN(@NewFundName) > 0
        BEGIN
            SELECT TOP 1 @OverriddenFundID = EMAFundID
            FROM dbo.OrganizationFund
            WHERE EMAOrganizationID = @OrgID AND Name = @NewFundName AND IsActive = 'Y';
        END

        IF @OverriddenFundID IS NOT NULL AND @OverriddenFundID > 0
        BEGIN
            INSERT INTO dbo.DonationDistribution (
                EMADonationID,
                EMACampusID,
                EMAFundID,
                DistributionAmount
            )
            VALUES (
                @EMADonationID,
                @CampusID,
                @OverriddenFundID,
                @DonationAmount
            );

            RETURN;
        END
    END

    -- Check prior donation for this donor (Priority 1: Template)
    SELECT TOP 1 
        @PriorDonationID = d.EMADonationID,
        @PriorDonationAmount = d.DonationAmount,
        @PriorIsTemplate = ISNULL(d.IsTemplate, 'N')
    FROM dbo.Donation d
    WHERE d.EMAOrganizationID = @OrgID 
      AND d.EMADonorID = @DonorID 
      AND d.EMADonationID != @EMADonationID
    ORDER BY d.DateTimeReceived DESC, d.EMADonationID DESC;

    SELECT @PriorSplitCount = COUNT(*)
    FROM dbo.DonationDistribution
    WHERE EMADonationID = @PriorDonationID;

    -- BRANCH 2: Non-Templated Donations (Hierarchical Fallback)
    IF @PriorDonationID IS NULL OR @PriorIsTemplate != 'Y' OR @PriorSplitCount IS NULL OR @PriorSplitCount = 0
    BEGIN
        DECLARE @TargetFundID INT = NULL,
                @TargetCampusID INT = @CampusID,
                @StepCode INT = 88;

        -- Priority 2: Donor Default Fund
        SELECT TOP 1 
            @TargetFundID = f.EMAFundID,
            @TargetCampusID = ISNULL(d.EMADefaultCampusID, @CampusID)
        FROM dbo.Donor d
        JOIN dbo.OrganizationFund f ON f.EMAFundID = d.EMADefaultFundID 
                                   AND f.EMAOrganizationID = @OrgID 
                                   AND f.IsActive = 'Y'
        WHERE d.EMADonorID = @DonorID 
          AND d.EMAOrganizationID = @OrgID;

        IF @TargetFundID IS NOT NULL
        BEGIN
            SET @StepCode = 84; -- Status 84: Donor Default Fund Applied
        END
        ELSE
        BEGIN
            -- Priority 3: Campus Default Fund
            SELECT TOP 1 
                @TargetFundID = f.EMAFundID,
                @TargetCampusID = c.EMACampusID
            FROM dbo.Donor d
            JOIN dbo.OrganizationCampus c ON c.EMACampusID = d.EMADefaultCampusID 
                                         AND c.EMAOrganizationID = @OrgID 
                                         AND c.IsActive = 'Y'
            JOIN dbo.OrganizationFund f ON f.EMAFundID = c.DefaultCampusFundID 
                                       AND f.EMAOrganizationID = @OrgID 
                                       AND f.IsActive = 'Y'
            WHERE d.EMADonorID = @DonorID 
              AND d.EMAOrganizationID = @OrgID;

            IF @TargetFundID IS NOT NULL
            BEGIN
                SET @StepCode = 86; -- Status 86: Campus Default Fund Applied
            END
            ELSE
            BEGIN
                -- Priority 4: Organization Default Fund
                SET @TargetFundID = @OrgDefaultFundID;
                SET @StepCode = 88; -- Status 88: Organization Default Fund Applied

                IF @TargetFundID IS NULL
                BEGIN
                    SELECT TOP 1 @TargetFundID = EMAFundID
                    FROM dbo.OrganizationFund
                    WHERE EMAOrganizationID = @OrgID AND IsActive = 'Y'
                    ORDER BY Sequence, Name;
                END
            END
        END

        IF @TargetFundID IS NOT NULL
        BEGIN
            INSERT INTO dbo.DonationDistribution (
                EMADonationID,
                EMACampusID,
                EMAFundID,
                DistributionAmount
            )
            VALUES (
                @EMADonationID,
                @TargetCampusID,
                @TargetFundID,
                @DonationAmount
            );

            INSERT INTO dbo.DonationStepLog (
                EMADonationID,
                DonationStepID,
                DateTimeLogged
            )
            VALUES (
                @EMADonationID,
                @StepCode,
                SYSUTCDATETIME()
            );
        END

        RETURN;
    END

    -- BRANCH 3: Templated & Same Amount -> Exact Match
    IF @PriorIsTemplate = 'Y' AND @PriorDonationAmount = @DonationAmount
    BEGIN
        INSERT INTO dbo.DonationDistribution (
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
        FROM dbo.DonationDistribution
        WHERE EMADonationID = @PriorDonationID;

        UPDATE dbo.Donation
        SET IsTemplate = 'Y'
        WHERE EMADonationID = @EMADonationID;

        -- Status 90: Donation Template Applied with No Proration
        INSERT INTO dbo.DonationStepLog (
            EMADonationID,
            DonationStepID,
            DateTimeLogged
        )
        VALUES (
            @EMADonationID,
            90,
            SYSUTCDATETIME()
        );

        RETURN;
    END

    -- BRANCH 4: Templated & Different Amount -> Prorated with Excess Penny to Last Row
    IF @PriorIsTemplate = 'Y' AND @PriorDonationAmount != @DonationAmount AND @PriorDonationAmount > 0
    BEGIN
        DECLARE @TempSplits TABLE (
            RowNumber INT IDENTITY(1,1),
            EMACampusID INT,
            EMAFundID INT,
            OriginalAmount MONEY,
            ProratedAmount MONEY
        );

        INSERT INTO @TempSplits (EMACampusID, EMAFundID, OriginalAmount, ProratedAmount)
        SELECT 
            EMACampusID,
            EMAFundID,
            DistributionAmount,
            ROUND(@DonationAmount * (DistributionAmount / @PriorDonationAmount), 2)
        FROM dbo.DonationDistribution
        WHERE EMADonationID = @PriorDonationID
        ORDER BY EMADonationDistributionID ASC;

        DECLARE @SumProrated MONEY,
                @Difference MONEY,
                @MaxRow INT;

        SELECT @SumProrated = SUM(ProratedAmount), @MaxRow = MAX(RowNumber)
        FROM @TempSplits;

        SET @Difference = @DonationAmount - @SumProrated;

        IF @Difference != 0 AND @MaxRow IS NOT NULL
        BEGIN
            UPDATE @TempSplits
            SET ProratedAmount = ProratedAmount + @Difference
            WHERE RowNumber = @MaxRow;
        END

        INSERT INTO dbo.DonationDistribution (
            EMADonationID,
            EMACampusID,
            EMAFundID,
            DistributionAmount
        )
        SELECT 
            @EMADonationID,
            EMACampusID,
            EMAFundID,
            ProratedAmount
        FROM @TempSplits;

        UPDATE dbo.Donation
        SET IsTemplate = 'Y'
        WHERE EMADonationID = @EMADonationID;

        -- Status 95: Donation Template Applied with Proration
        INSERT INTO dbo.DonationStepLog (
            EMADonationID,
            DonationStepID,
            DateTimeLogged
        )
        VALUES (
            @EMADonationID,
            95,
            SYSUTCDATETIME()
        );

        RETURN;
    END
END
GO
