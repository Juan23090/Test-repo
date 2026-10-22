-- sp_PrepareMailingListInstance.sql
-- Stored Procedure to generate a mailing list instance based on donor criteria, opt-in, and opt-out logic.

CREATE OR ALTER PROCEDURE dbo.sp_PrepareMailingListInstance
    @EMAOrganizationID INT,
    @MailingListID INT,
    @CreatedByUserID INT = NULL,
    @NewInstanceID INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- Validate mailing list exists
    IF NOT EXISTS (
        SELECT 1 FROM dbo.OrganizationMailingList
        WHERE MailingListID = @MailingListID AND EMAOrganizationID = @EMAOrganizationID
    )
    BEGIN
        THROW 53001, 'Mailing list does not exist for this organization.', 1;
    END;

    -- Fetch List Configuration
    DECLARE @Donation1StartDate DATE, @Donation1EndDate DATE, @Donation1MoneyFrom DECIMAL(18,2), @Donation1MoneyTo DECIMAL(18,2);
    DECLARE @Donation2StartDate DATE, @Donation2EndDate DATE, @Donation2MoneyFrom DECIMAL(18,2), @Donation2MoneyTo DECIMAL(18,2);
    DECLARE @AndOr VARCHAR(10), @SendMethodID INT;

    SELECT 
        @Donation1StartDate = Donation1StartDate,
        @Donation1EndDate = Donation1EndDate,
        @Donation1MoneyFrom = Donation1MoneyFrom,
        @Donation1MoneyTo = Donation1MoneyTo,
        @AndOr = ISNULL(AndOr, 'And'),
        @Donation2StartDate = Donation2StartDate,
        @Donation2EndDate = Donation2EndDate,
        @Donation2MoneyFrom = Donation2MoneyFrom,
        @Donation2MoneyTo = Donation2MoneyTo,
        @SendMethodID = SendMethodID
    FROM dbo.OrganizationMailingList
    WHERE MailingListID = @MailingListID AND EMAOrganizationID = @EMAOrganizationID;

    -- Temp tables for evaluation
    CREATE TABLE #SelectedDonors (EMADonorID INT PRIMARY KEY);
    CREATE TABLE #OptedInDonors (EMADonorID INT PRIMARY KEY);
    CREATE TABLE #OptedOutDonors (EMADonorID INT PRIMARY KEY);
    CREATE TABLE #FinalSendList (EMADonorID INT PRIMARY KEY);

    -- 1. Evaluate Selected Donors based on giving history
    -- Donation Criteria 1 Match
    -- If ALL Donation 1 criteria are NULL, select all donors matching the send method (no donation required).
    -- Otherwise, require at least one donation matching the specified filters.
    IF @Donation1StartDate IS NULL AND @Donation1EndDate IS NULL AND @Donation1MoneyFrom IS NULL AND @Donation1MoneyTo IS NULL
    BEGIN
        SELECT DISTINCT d.EMADonorID
        INTO #Cond1Donors
        FROM dbo.Donor d
        WHERE d.EMAOrganizationID = @EMAOrganizationID
          AND (
              (@SendMethodID = 1 AND d.EmailAddress IS NOT NULL AND LTRIM(RTRIM(d.EmailAddress)) <> '')
              OR
              (@SendMethodID = 2 AND d.Address IS NOT NULL AND LTRIM(RTRIM(d.Address)) <> '')
              OR
              (@SendMethodID IS NULL)
          );
    END
    ELSE
    BEGIN
        SELECT DISTINCT d.EMADonorID
        INTO #Cond1Donors
        FROM dbo.Donor d
        INNER JOIN dbo.Donation don ON d.EMAOrganizationID = don.EMAOrganizationID AND d.EMADonorID = don.EMADonorID
        WHERE d.EMAOrganizationID = @EMAOrganizationID
          AND (@Donation1StartDate IS NULL OR don.DateTimeReceived >= @Donation1StartDate)
          AND (@Donation1EndDate IS NULL OR don.DateTimeReceived <= @Donation1EndDate)
          AND (@Donation1MoneyFrom IS NULL OR don.DonationAmount >= @Donation1MoneyFrom)
          AND (@Donation1MoneyTo IS NULL OR don.DonationAmount <= @Donation1MoneyTo)
          AND (
              (@SendMethodID = 1 AND d.EmailAddress IS NOT NULL AND LTRIM(RTRIM(d.EmailAddress)) <> '')
              OR
              (@SendMethodID = 2 AND d.Address IS NOT NULL AND LTRIM(RTRIM(d.Address)) <> '')
              OR
              (@SendMethodID IS NULL)
          );
    END;

    -- Donation Criteria 2 Match
    -- If ALL Donation 2 criteria are NULL, select all donors matching the send method (no donation required).
    IF @Donation2StartDate IS NULL AND @Donation2EndDate IS NULL AND @Donation2MoneyFrom IS NULL AND @Donation2MoneyTo IS NULL
    BEGIN
        SELECT DISTINCT d.EMADonorID
        INTO #Cond2Donors
        FROM dbo.Donor d
        WHERE d.EMAOrganizationID = @EMAOrganizationID
          AND (
              (@SendMethodID = 1 AND d.EmailAddress IS NOT NULL AND LTRIM(RTRIM(d.EmailAddress)) <> '')
              OR
              (@SendMethodID = 2 AND d.Address IS NOT NULL AND LTRIM(RTRIM(d.Address)) <> '')
              OR
              (@SendMethodID IS NULL)
          );
    END
    ELSE
    BEGIN
        SELECT DISTINCT d.EMADonorID
        INTO #Cond2Donors
        FROM dbo.Donor d
        INNER JOIN dbo.Donation don ON d.EMAOrganizationID = don.EMAOrganizationID AND d.EMADonorID = don.EMADonorID
        WHERE d.EMAOrganizationID = @EMAOrganizationID
          AND (@Donation2StartDate IS NULL OR don.DateTimeReceived >= @Donation2StartDate)
          AND (@Donation2EndDate IS NULL OR don.DateTimeReceived <= @Donation2EndDate)
          AND (@Donation2MoneyFrom IS NULL OR don.DonationAmount >= @Donation2MoneyFrom)
          AND (@Donation2MoneyTo IS NULL OR don.DonationAmount <= @Donation2MoneyTo)
          AND (
              (@SendMethodID = 1 AND d.EmailAddress IS NOT NULL AND LTRIM(RTRIM(d.EmailAddress)) <> '')
              OR
              (@SendMethodID = 2 AND d.Address IS NOT NULL AND LTRIM(RTRIM(d.Address)) <> '')
              OR
              (@SendMethodID IS NULL)
          );
    END;

    IF @AndOr = 'And' AND (
        @Donation2StartDate IS NOT NULL 
        OR @Donation2EndDate IS NOT NULL 
        OR @Donation2MoneyFrom IS NOT NULL 
        OR @Donation2MoneyTo IS NOT NULL
    )
    BEGIN
        INSERT INTO #SelectedDonors (EMADonorID)
        SELECT c1.EMADonorID FROM #Cond1Donors c1
        INNER JOIN #Cond2Donors c2 ON c1.EMADonorID = c2.EMADonorID;
    END
    ELSE IF @AndOr = 'Or'
    BEGIN
        INSERT INTO #SelectedDonors (EMADonorID)
        SELECT EMADonorID FROM #Cond1Donors
        UNION
        SELECT EMADonorID FROM #Cond2Donors;
    END
    ELSE
    BEGIN
        INSERT INTO #SelectedDonors (EMADonorID)
        SELECT EMADonorID FROM #Cond1Donors;
    END;

    -- 2. Identify Opt-In & Opt-Out Donors
    INSERT INTO #OptedInDonors (EMADonorID)
    SELECT EMADonorID FROM dbo.OrganizationMailingListDonor
    WHERE EMAOrganizationID = @EMAOrganizationID AND MailingListID = @MailingListID AND OptedIn = 1;

    INSERT INTO #OptedOutDonors (EMADonorID)
    SELECT EMADonorID FROM dbo.OrganizationMailingListDonor
    WHERE EMAOrganizationID = @EMAOrganizationID AND MailingListID = @MailingListID AND OptedOut = 1;

    -- 3. Calculate Final Send List based on Truth Table:
    -- - Opt Out overrides EVERYTHING (if OptedOut = 1, NOT in Send List)
    -- - Opt In means doesn't need to be selected (if OptedIn = 1 AND OptedOut = 0 -> in Send List)
    -- - If OptedIn = 0 AND OptedOut = 0 AND Selected = 1 AND OptedIn = 1 -> in Send List
    INSERT INTO #FinalSendList (EMADonorID)
    SELECT DISTINCT d.EMADonorID
    FROM dbo.Donor d
    LEFT JOIN #SelectedDonors sel ON d.EMADonorID = sel.EMADonorID
    LEFT JOIN #OptedInDonors optin ON d.EMADonorID = optin.EMADonorID
    LEFT JOIN #OptedOutDonors optout ON d.EMADonorID = optout.EMADonorID
    WHERE d.EMAOrganizationID = @EMAOrganizationID
      AND ISNULL(optout.EMADonorID, 0) = 0 -- NOT Opted Out
      AND (
          ISNULL(optin.EMADonorID, 0) > 0  -- Opted In overrides selection requirement
          OR
          ISNULL(sel.EMADonorID, 0) > 0    -- Meets donation criteria selection
      )
      -- Filter out missing email or address depending on SendMethodID (1 = Email, 2 = Postal)
      AND (
          (@SendMethodID = 1 AND d.EmailAddress IS NOT NULL AND LTRIM(RTRIM(d.EmailAddress)) <> '')
          OR
          (@SendMethodID = 2 AND d.Address IS NOT NULL AND LTRIM(RTRIM(d.Address)) <> '')
          OR
          (@SendMethodID IS NULL)
      );

    -- 4. Create Instance Record
    DECLARE @CountSelected INT = (SELECT COUNT(*) FROM #SelectedDonors);
    DECLARE @CountOptIn INT = (SELECT COUNT(*) FROM #OptedInDonors);
    DECLARE @CountOptOut INT = (SELECT COUNT(*) FROM #OptedOutDonors);
    DECLARE @CountFinal INT = (SELECT COUNT(*) FROM #FinalSendList);

    INSERT INTO dbo.OrganizationMailingListInstance (
        OrganizationID, MailingListID, DateTimeCreated, DonorsSelected, DonorsOptedIn, DonorsOptedOut
    )
    VALUES (
        @EMAOrganizationID, @MailingListID, GETDATE(), @CountSelected, @CountOptIn, @CountOptOut
    );

    SET @NewInstanceID = SCOPE_IDENTITY();

    -- 5. Create Snapshot Records in OrganizationMailingListInstanceDonor
    INSERT INTO dbo.OrganizationMailingListInstanceDonor (
        EMAOrganizationID, MailingListID, MailingListInstanceID, EMADonorID,
        EmailAddress, OfficialDonorName, Address, City, ProvinceStateCode, PostalCode, Country, DateTimeCreated
    )
    SELECT 
        d.EMAOrganizationID, @MailingListID, @NewInstanceID, d.EMADonorID,
        d.EmailAddress, d.OfficialDonorName, d.Address, d.City, d.ProvinceStateCode, d.PostalCode, d.Country, GETDATE()
    FROM #FinalSendList f
    INNER JOIN dbo.Donor d ON d.EMAOrganizationID = @EMAOrganizationID AND d.EMADonorID = f.EMADonorID;

    -- 6. Update Summary Totals on Main List
    UPDATE dbo.OrganizationMailingList
    SET NumberDonorsExtracted = @CountSelected,
        NumberDonorsOptedIn = @CountOptIn,
        NumberDonorsOptedOut = @CountOptOut,
        NumberDonorsInMailingList = @CountFinal,
        DateTimeUpdated = GETDATE(),
        UpdatedByUserID = @CreatedByUserID
    WHERE MailingListID = @MailingListID AND EMAOrganizationID = @EMAOrganizationID;

    -- Cleanup
    DROP TABLE #SelectedDonors;
    DROP TABLE #OptedInDonors;
    DROP TABLE #OptedOutDonors;
    DROP TABLE #FinalSendList;
    DROP TABLE #Cond1Donors;
    DROP TABLE #Cond2Donors;
END;
