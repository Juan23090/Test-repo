/*
    EMA Demo Organization bootstrap
    -------------------------------
    Creates a completely independent permanent demo organization.

    This script does NOT clone Organization 1 (or any other organization).
    Every organization-specific configuration row, donor, donation, and
    distribution created here belongs only to EMA Demo Org.

    Result:
      - one EMA Demo Org promoted to OrganizationStatusID = 5000
      - 2 campuses
      - 5 funds
      - 6 methods of giving
      - 4 donation sources
      - 5 events
      - 3 tribute types
      - 1 anonymous donor
      - 25 normal fictional donors
      - exactly 100 fictional donations over roughly two years
      - matching tenant-owned distributions
      - one dedicated active template admin user
      - OrganizationUserStatusID = 10
      - RoleID = 10

    The organization is inserted temporarily with OrganizationStatusID = 0 so
    TR_Organization_AfterInsert exits through the sandbox guard and does not
    enforce normal onboarding journey requirements. After all template data and
    the dedicated template admin membership are created, the organization is
    promoted to status 5000.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

DECLARE
    @DemoOrganizationName VARCHAR(255) = 'EMA Demo Org',
    @SandboxStatusID SMALLINT = 0,
    @DemoStatusID SMALLINT = 5000,
    @TemplateRoleID INT = 10,
    @OrganizationUserStatusID SMALLINT = 10,
    @Now DATETIME2(7) = SYSUTCDATETIME(),
    @DemoOrganizationID INT,
    @DemoUserID INT;

/* -------------------------------------------------------------------------
   Existing-template guard
   ------------------------------------------------------------------------- */

SELECT TOP (1)
    @DemoOrganizationID = EMAOrganizationID
FROM dbo.Organization
WHERE OrganizationName = @DemoOrganizationName
ORDER BY EMAOrganizationID;

IF @DemoOrganizationID IS NOT NULL
BEGIN
    SELECT TOP (1)
        @DemoUserID = ou.UserID
    FROM dbo.OrganizationUser ou
    INNER JOIN dbo.[User] u
        ON u.UserID = ou.UserID
    INNER JOIN dbo.OrganizationUserRole ur
        ON ur.EMAOrganizationID = ou.EMAOrganizationID
       AND ur.UserID = ou.UserID
    WHERE ou.EMAOrganizationID = @DemoOrganizationID
      AND u.IsActive = 1
    GROUP BY ou.UserID
    ORDER BY
        COUNT(DISTINCT ur.RoleID) DESC,
        ou.UserID;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.Organization
        WHERE EMAOrganizationID = @DemoOrganizationID
          AND OrganizationStatusID = @DemoStatusID
    )
        THROW 51100,
            'EMA Demo Org already exists but is not OrganizationStatusID 5000.',
            1;

    IF
    (
        SELECT COUNT(*)
        FROM dbo.Donor
        WHERE EMAOrganizationID = @DemoOrganizationID
    ) <> 26
        THROW 51101,
            'EMA Demo Org already exists but does not contain exactly 26 donors (25 normal plus anonymous).',
            1;

    IF
    (
        SELECT COUNT(*)
        FROM dbo.Donation
        WHERE EMAOrganizationID = @DemoOrganizationID
    ) <> 100
        THROW 51102,
            'EMA Demo Org already exists but does not contain exactly 100 donations.',
            1;

    IF @DemoUserID IS NULL
        THROW 51103,
            'EMA Demo Org already exists but no active template user with a role could be resolved.',
            1;

    SELECT
        @DemoOrganizationID AS SandboxTemplateOrganizationID,
        @DemoUserID AS SandboxTemplateUserID,
        organization.OrganizationName,
        organization.OrganizationStatusID,
        COUNT(DISTINCT donor.EMADonorID) AS TotalDonors,
        COUNT(DISTINCT donation.EMADonationID) AS TotalDonations,
        MIN(donation.DateTimeReceived) AS OldestDonation,
        MAX(donation.DateTimeReceived) AS NewestDonation
    FROM dbo.Organization organization
    LEFT JOIN dbo.Donor donor
        ON donor.EMAOrganizationID = organization.EMAOrganizationID
    LEFT JOIN dbo.Donation donation
        ON donation.EMAOrganizationID = organization.EMAOrganizationID
    WHERE organization.EMAOrganizationID = @DemoOrganizationID
    GROUP BY
        organization.OrganizationName,
        organization.OrganizationStatusID;

    RETURN;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.OrganizationStatus
    WHERE OrganizationStatusID = @SandboxStatusID
)
    THROW 51104,
        'OrganizationStatusID 0 must exist before creating EMA Demo Org.',
        1;

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.OrganizationStatus
    WHERE OrganizationStatusID = @DemoStatusID
)
    THROW 51105,
        'OrganizationStatusID 5000 must exist before creating EMA Demo Org.',
        1;

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.[Role]
    WHERE RoleID = @TemplateRoleID
      AND ISNULL(IsActive, 'Y') = 'Y'
)
    THROW 51106,
        'RoleID 10 must exist and be active before creating EMA Demo Org.',
        1;

IF OBJECT_ID('dbo.TR_Organization_AfterInsert', 'TR') IS NULL
    THROW 51107,
        'TR_Organization_AfterInsert must exist before creating EMA Demo Org.',
        1;

IF CHARINDEX(
       N'EMA-SANDBOX-GUARD',
       OBJECT_DEFINITION(OBJECT_ID('dbo.TR_Organization_AfterInsert'))
   ) = 0
    THROW 51108,
        'Run 20260819_SandboxDemoOrgCompatibility.sql before creating EMA Demo Org so the Organization trigger skips status 0 rows.',
        1;

BEGIN TRANSACTION;

BEGIN TRY
    /* ---------------------------------------------------------------------
       Organization
       --------------------------------------------------------------------- */

    INSERT INTO dbo.Organization
    (
        OrganizationName,
        AdminName,
        AdminEmail,
        AdminEmailVerified,
        AdminPhone,
        AdminPhoneVerified,
        ContactName,
        ContactEmail,
        ContactEmailVerified,
        ContactPhone,
        OrganizationPhone,
        OrganizationEmail,
        OrganizationEmailVerified,
        OrganizationWebsite,
        Address,
        City,
        ProvinceStateCode,
        PostalCode,
        AnnualBudget,
        DonorRoleId,
        KPIDefaultHistoryDays,
        KPIGraphOrganizationLandingPage,
        KPIGraphDonorLandingPage,
        MaxHoursToAllowFundChanges,
        UseAnonymous,
        UseCampuses,
        UseEvents,
        UseFunds,
        UseMethodsOfGiving,
        UseSources,
        UseTributeTypes,
        OrganizationStatusID,
        DateTimeCreated,
        DateTimeUpdated
    )
    VALUES
    (
        @DemoOrganizationName,
        N'EMA Demo Administrator',
        N'ema-demo-admin@example.com',
        'Y',
        '7805550100',
        'Y',
        N'EMA Demo Contact',
        N'ema-demo-contact@example.com',
        'Y',
        '7805550101',
        '7805550102',
        N'ema-demo@example.com',
        'Y',
        'https://example.com/ema-demo',
        '100 Demo Avenue',
        'Edmonton',
        'AB',
        'T5J 0N3',
        '1000000',
        3000,
        30,
        'M',
        'V',
        48,
        'Y',
        'Y',
        'Y',
        'Y',
        'Y',
        'Y',
        'Y',
        @SandboxStatusID,
        @Now,
        @Now
    );

    SET @DemoOrganizationID = CONVERT(INT, SCOPE_IDENTITY());

    /* ---------------------------------------------------------------------
       Dedicated template admin user, membership, and role
       --------------------------------------------------------------------- */

    INSERT INTO dbo.[User]
    (
        UserName,
        EmailAddress,
        EmailAddressValidated,
        PhoneNumber,
        IsActive,
        DateTimeCreated,
        DateTimeUpdated
    )
    VALUES
    (
        N'EMA Demo Administrator',
        N'ema-demo-admin@example.invalid',
        'Y',
        '780-555-0100',
        1,
        @Now,
        @Now
    );

    SET @DemoUserID = CONVERT(INT, SCOPE_IDENTITY());

    INSERT INTO dbo.OrganizationUser
    (
        OrganizationUserID,
        EMAOrganizationID,
        UserID,
        OrganizationUserStatusID
    )
    VALUES
    (
        NEXT VALUE FOR dbo.OrganizationUserIDSequence,
        @DemoOrganizationID,
        @DemoUserID,
        @OrganizationUserStatusID
    );

    INSERT INTO dbo.OrganizationUserRole
    (
        EMAOrganizationID,
        UserID,
        RoleID
    )
    VALUES
    (
        @DemoOrganizationID,
        @DemoUserID,
        @TemplateRoleID
    );

    /* ---------------------------------------------------------------------
       Campuses
       --------------------------------------------------------------------- */

    DECLARE
        @MainCampusID INT,
        @NorthCampusID INT;

    INSERT INTO dbo.OrganizationCampus
    (
        EMAOrganizationID,
        ChMSCampusID,
        [Sequence],
        Name,
        IsActive,
        DateTimeCreated,
        DateTimeUpdated
    )
    VALUES
    (
        @DemoOrganizationID,
        'DEMO-CAMPUS-001',
        1,
        'Main Campus',
        'Y',
        @Now,
        @Now
    );

    SET @MainCampusID = CONVERT(INT, SCOPE_IDENTITY());

    INSERT INTO dbo.OrganizationCampus
    (
        EMAOrganizationID,
        ChMSCampusID,
        [Sequence],
        Name,
        IsActive,
        DateTimeCreated,
        DateTimeUpdated
    )
    VALUES
    (
        @DemoOrganizationID,
        'DEMO-CAMPUS-002',
        2,
        'North Campus',
        'Y',
        @Now,
        @Now
    );

    SET @NorthCampusID = CONVERT(INT, SCOPE_IDENTITY());

    /* ---------------------------------------------------------------------
       Funds
       --------------------------------------------------------------------- */

    DECLARE @Funds TABLE
    (
        RowNumber INT NOT NULL PRIMARY KEY,
        EMAFundID INT NOT NULL UNIQUE,
        Name VARCHAR(255) NOT NULL
    );

    DECLARE
        @GeneralFundID INT,
        @BuildingFundID INT,
        @YouthFundID INT,
        @MissionsFundID INT,
        @CommunityFundID INT;

    INSERT INTO dbo.OrganizationFund
    (
        EMAOrganizationID,
        ChMSFundID,
        [Sequence],
        Name,
        GLAccountNumber,
        TaxDeductible,
        EMACampusID,
        IsActive,
        DateTimeCreated,
        DateTimeUpdated
    )
    VALUES
    (@DemoOrganizationID, 'DEMO-FUND-001', 1, 'General Fund',       '4000', 'Y', NULL, 'Y', @Now, @Now);
    SET @GeneralFundID = CONVERT(INT, SCOPE_IDENTITY());

    INSERT INTO dbo.OrganizationFund
    (
        EMAOrganizationID, ChMSFundID, [Sequence], Name,
        GLAccountNumber, TaxDeductible, EMACampusID, IsActive,
        DateTimeCreated, DateTimeUpdated
    )
    VALUES
    (@DemoOrganizationID, 'DEMO-FUND-002', 2, 'Building Fund',      '4010', 'Y', NULL, 'Y', @Now, @Now);
    SET @BuildingFundID = CONVERT(INT, SCOPE_IDENTITY());

    INSERT INTO dbo.OrganizationFund
    (
        EMAOrganizationID, ChMSFundID, [Sequence], Name,
        GLAccountNumber, TaxDeductible, EMACampusID, IsActive,
        DateTimeCreated, DateTimeUpdated
    )
    VALUES
    (@DemoOrganizationID, 'DEMO-FUND-003', 3, 'Youth Fund',         '4020', 'Y', NULL, 'Y', @Now, @Now);
    SET @YouthFundID = CONVERT(INT, SCOPE_IDENTITY());

    INSERT INTO dbo.OrganizationFund
    (
        EMAOrganizationID, ChMSFundID, [Sequence], Name,
        GLAccountNumber, TaxDeductible, EMACampusID, IsActive,
        DateTimeCreated, DateTimeUpdated
    )
    VALUES
    (@DemoOrganizationID, 'DEMO-FUND-004', 4, 'Missions Fund',      '4030', 'Y', NULL, 'Y', @Now, @Now);
    SET @MissionsFundID = CONVERT(INT, SCOPE_IDENTITY());

    INSERT INTO dbo.OrganizationFund
    (
        EMAOrganizationID, ChMSFundID, [Sequence], Name,
        GLAccountNumber, TaxDeductible, EMACampusID, IsActive,
        DateTimeCreated, DateTimeUpdated
    )
    VALUES
    (@DemoOrganizationID, 'DEMO-FUND-005', 5, 'Community Outreach', '4040', 'Y', NULL, 'Y', @Now, @Now);
    SET @CommunityFundID = CONVERT(INT, SCOPE_IDENTITY());

    INSERT INTO @Funds (RowNumber, EMAFundID, Name)
    VALUES
        (1, @GeneralFundID,   'General Fund'),
        (2, @BuildingFundID,  'Building Fund'),
        (3, @YouthFundID,     'Youth Fund'),
        (4, @MissionsFundID,  'Missions Fund'),
        (5, @CommunityFundID, 'Community Outreach');

    UPDATE dbo.OrganizationCampus
    SET DefaultCampusFundID = @GeneralFundID
    WHERE EMAOrganizationID = @DemoOrganizationID
      AND EMACampusID IN (@MainCampusID, @NorthCampusID);

    /* ---------------------------------------------------------------------
       Methods of giving
       --------------------------------------------------------------------- */

    DECLARE @Methods TABLE
    (
        RowNumber INT NOT NULL PRIMARY KEY,
        EMAMethodOfGivingID INT NOT NULL UNIQUE,
        Description VARCHAR(255) NOT NULL
    );

    DECLARE
        @EtransferMethodID INT,
        @CreditCardMethodID INT,
        @DebitCardMethodID INT,
        @ChequeMethodID INT,
        @CashMethodID INT,
        @PADMethodID INT;

    INSERT INTO dbo.OrganizationMethodOfGiving
    (
        EMAOrganizationID,
        ChMSMethodOfGivingID,
        [Sequence],
        Description,
        GLAccountNumber,
        AcknowledgeToDonor,
        AcknowledgeToOrganization,
        AcknowledgeToEMASupport,
        CRAReceiptable,
        JournalEntryInDetail,
        IsActive,
        DateTimeCreated,
        DateTimeUpdated
    )
    VALUES
    (@DemoOrganizationID, 'DEMO-METHOD-001', 1, 'eTransfer',   '1000', 'Y', 'Y', 'N', 'Y', 'N', 'Y', @Now, @Now);
    SET @EtransferMethodID = CONVERT(INT, SCOPE_IDENTITY());

    INSERT INTO dbo.OrganizationMethodOfGiving
    (
        EMAOrganizationID, ChMSMethodOfGivingID, [Sequence], Description,
        GLAccountNumber, AcknowledgeToDonor, AcknowledgeToOrganization,
        AcknowledgeToEMASupport, CRAReceiptable, JournalEntryInDetail,
        IsActive, DateTimeCreated, DateTimeUpdated
    )
    VALUES
    (@DemoOrganizationID, 'DEMO-METHOD-002', 2, 'Credit Card', '1010', 'Y', 'Y', 'N', 'Y', 'N', 'Y', @Now, @Now);
    SET @CreditCardMethodID = CONVERT(INT, SCOPE_IDENTITY());

    INSERT INTO dbo.OrganizationMethodOfGiving
    (
        EMAOrganizationID, ChMSMethodOfGivingID, [Sequence], Description,
        GLAccountNumber, AcknowledgeToDonor, AcknowledgeToOrganization,
        AcknowledgeToEMASupport, CRAReceiptable, JournalEntryInDetail,
        IsActive, DateTimeCreated, DateTimeUpdated
    )
    VALUES
    (@DemoOrganizationID, 'DEMO-METHOD-003', 3, 'Debit Card',  '1020', 'Y', 'Y', 'N', 'Y', 'N', 'Y', @Now, @Now);
    SET @DebitCardMethodID = CONVERT(INT, SCOPE_IDENTITY());

    INSERT INTO dbo.OrganizationMethodOfGiving
    (
        EMAOrganizationID, ChMSMethodOfGivingID, [Sequence], Description,
        GLAccountNumber, AcknowledgeToDonor, AcknowledgeToOrganization,
        AcknowledgeToEMASupport, CRAReceiptable, JournalEntryInDetail,
        IsActive, DateTimeCreated, DateTimeUpdated
    )
    VALUES
    (@DemoOrganizationID, 'DEMO-METHOD-004', 4, 'Cheque',      '1030', 'Y', 'Y', 'N', 'Y', 'N', 'Y', @Now, @Now);
    SET @ChequeMethodID = CONVERT(INT, SCOPE_IDENTITY());

    INSERT INTO dbo.OrganizationMethodOfGiving
    (
        EMAOrganizationID, ChMSMethodOfGivingID, [Sequence], Description,
        GLAccountNumber, AcknowledgeToDonor, AcknowledgeToOrganization,
        AcknowledgeToEMASupport, CRAReceiptable, JournalEntryInDetail,
        IsActive, DateTimeCreated, DateTimeUpdated
    )
    VALUES
    (@DemoOrganizationID, 'DEMO-METHOD-005', 5, 'Cash',        '1040', 'N', 'Y', 'N', 'Y', 'N', 'Y', @Now, @Now);
    SET @CashMethodID = CONVERT(INT, SCOPE_IDENTITY());

    INSERT INTO dbo.OrganizationMethodOfGiving
    (
        EMAOrganizationID, ChMSMethodOfGivingID, [Sequence], Description,
        GLAccountNumber, AcknowledgeToDonor, AcknowledgeToOrganization,
        AcknowledgeToEMASupport, CRAReceiptable, JournalEntryInDetail,
        IsActive, DateTimeCreated, DateTimeUpdated
    )
    VALUES
    (@DemoOrganizationID, 'DEMO-METHOD-006', 6, 'PAD',         '1050', 'Y', 'Y', 'N', 'Y', 'N', 'Y', @Now, @Now);
    SET @PADMethodID = CONVERT(INT, SCOPE_IDENTITY());

    INSERT INTO @Methods (RowNumber, EMAMethodOfGivingID, Description)
    VALUES
        (1, @EtransferMethodID,  'eTransfer'),
        (2, @CreditCardMethodID, 'Credit Card'),
        (3, @DebitCardMethodID,  'Debit Card'),
        (4, @ChequeMethodID,     'Cheque'),
        (5, @CashMethodID,       'Cash'),
        (6, @PADMethodID,        'PAD');

    /* ---------------------------------------------------------------------
       Donation sources
       --------------------------------------------------------------------- */

    DECLARE @Sources TABLE
    (
        RowNumber INT NOT NULL PRIMARY KEY,
        EMADonationSourceID INT NOT NULL UNIQUE,
        Description VARCHAR(255) NOT NULL
    );

    DECLARE
        @OnlineSourceID INT,
        @InPersonSourceID INT,
        @MailSourceID INT,
        @SpecialEventSourceID INT;

    INSERT INTO dbo.OrganizationDonationSource
    (
        EMAOrganizationID,
        ChMSDonationSource,
        [Sequence],
        Description,
        IsActive,
        DateTimeCreated,
        DateTimeUpdated
    )
    VALUES
    (@DemoOrganizationID, 1001, 1, 'Online Giving', 'Y', @Now, @Now);
    SET @OnlineSourceID = CONVERT(INT, SCOPE_IDENTITY());

    INSERT INTO dbo.OrganizationDonationSource
    (EMAOrganizationID, ChMSDonationSource, [Sequence], Description, IsActive, DateTimeCreated, DateTimeUpdated)
    VALUES
    (@DemoOrganizationID, 1002, 2, 'In Person', 'Y', @Now, @Now);
    SET @InPersonSourceID = CONVERT(INT, SCOPE_IDENTITY());

    INSERT INTO dbo.OrganizationDonationSource
    (EMAOrganizationID, ChMSDonationSource, [Sequence], Description, IsActive, DateTimeCreated, DateTimeUpdated)
    VALUES
    (@DemoOrganizationID, 1003, 3, 'Mail', 'Y', @Now, @Now);
    SET @MailSourceID = CONVERT(INT, SCOPE_IDENTITY());

    INSERT INTO dbo.OrganizationDonationSource
    (EMAOrganizationID, ChMSDonationSource, [Sequence], Description, IsActive, DateTimeCreated, DateTimeUpdated)
    VALUES
    (@DemoOrganizationID, 1004, 4, 'Special Event', 'Y', @Now, @Now);
    SET @SpecialEventSourceID = CONVERT(INT, SCOPE_IDENTITY());

    INSERT INTO @Sources (RowNumber, EMADonationSourceID, Description)
    VALUES
        (1, @OnlineSourceID,       'Online Giving'),
        (2, @InPersonSourceID,     'In Person'),
        (3, @MailSourceID,         'Mail'),
        (4, @SpecialEventSourceID, 'Special Event');

    /* ---------------------------------------------------------------------
       Events
       --------------------------------------------------------------------- */

    DECLARE @EventBaseID INT;

    SELECT @EventBaseID = ISNULL(MAX(EMAEventID), 0)
    FROM dbo.OrganizationEvent WITH (UPDLOCK, HOLDLOCK);

    DECLARE
        @GeneralEventID INT = @EventBaseID + 1,
        @SundayEventID INT = @EventBaseID + 2,
        @ChristmasEventID INT = @EventBaseID + 3,
        @YouthEventID INT = @EventBaseID + 4,
        @OutreachEventID INT = @EventBaseID + 5;

    INSERT INTO dbo.OrganizationEvent
    (
        EMAOrganizationID,
        EMAEventID,
        ChMSEventID,
        Description,
        IsActive,
        DateTimeCreated,
        DateTimeUpdated
    )
    VALUES
        (@DemoOrganizationID, @GeneralEventID,   'DEMO-EVENT-001', 'General Giving',           'Y', @Now, @Now),
        (@DemoOrganizationID, @SundayEventID,    'DEMO-EVENT-002', 'Sunday Service',           'Y', @Now, @Now),
        (@DemoOrganizationID, @ChristmasEventID, 'DEMO-EVENT-003', 'Christmas Campaign',       'Y', @Now, @Now),
        (@DemoOrganizationID, @YouthEventID,     'DEMO-EVENT-004', 'Youth Retreat',             'Y', @Now, @Now),
        (@DemoOrganizationID, @OutreachEventID,  'DEMO-EVENT-005', 'Community Outreach Drive', 'Y', @Now, @Now);

    /* ---------------------------------------------------------------------
       Tribute types
       --------------------------------------------------------------------- */

    DECLARE
        @InHonorTributeID INT,
        @InMemoryTributeID INT,
        @RecognitionTributeID INT;

    INSERT INTO dbo.OrganizationTributeType
    (
        EMAOrganizationID,
        ChMSTributeTypeID,
        [Sequence],
        Description,
        IsActive
    )
    VALUES
    (@DemoOrganizationID, 'DEMO-TRIBUTE-001', 1, 'In Honor Of', 'Y');
    SET @InHonorTributeID = CONVERT(INT, SCOPE_IDENTITY());

    INSERT INTO dbo.OrganizationTributeType
    (EMAOrganizationID, ChMSTributeTypeID, [Sequence], Description, IsActive)
    VALUES
    (@DemoOrganizationID, 'DEMO-TRIBUTE-002', 2, 'In Memory Of', 'Y');
    SET @InMemoryTributeID = CONVERT(INT, SCOPE_IDENTITY());

    INSERT INTO dbo.OrganizationTributeType
    (EMAOrganizationID, ChMSTributeTypeID, [Sequence], Description, IsActive)
    VALUES
    (@DemoOrganizationID, 'DEMO-TRIBUTE-003', 3, 'In Recognition Of', 'Y');
    SET @RecognitionTributeID = CONVERT(INT, SCOPE_IDENTITY());

    /* ---------------------------------------------------------------------
       Anonymous donor
       --------------------------------------------------------------------- */

    DECLARE @AnonymousDonorID INT;

    INSERT INTO dbo.Donor
    (
        EMAOrganizationID,
        ChMSDonorID,
        EMADefaultCampusID,
        GivingNumber,
        InteracDonorName,
        OfficialDonorName,
        Country,
        IsDonorPilotingEtransfers,
        AcknowledgmentForManualTransactions,
        AcknowledgementForAutomatedTransactions,
        DateTimeCreated,
        DateTimeUpdated
    )
    VALUES
    (
        @DemoOrganizationID,
        'DEMO-ANONYMOUS',
        @MainCampusID,
        'ANON',
        N'Anonymous Donor',
        N'Anonymous Donor',
        'Canada',
        'N',
        'N',
        'N',
        @Now,
        @Now
    );

    SET @AnonymousDonorID = CONVERT(INT, SCOPE_IDENTITY());

    /* ---------------------------------------------------------------------
       25 completely new fictional donors
       --------------------------------------------------------------------- */

    DECLARE @DonorNames TABLE
    (
        RowNumber INT NOT NULL PRIMARY KEY,
        OfficialDonorName NVARCHAR(510) NOT NULL
    );

    INSERT INTO @DonorNames (RowNumber, OfficialDonorName)
    VALUES
        (1,  N'Alex Mercer'),
        (2,  N'Jordan Bennett'),
        (3,  N'Taylor Singh'),
        (4,  N'Morgan Chen'),
        (5,  N'Casey Martin'),
        (6,  N'Riley Thompson'),
        (7,  N'Avery Wilson'),
        (8,  N'Jamie Patel'),
        (9,  N'Cameron Scott'),
        (10, N'Drew Anderson'),
        (11, N'Quinn Campbell'),
        (12, N'Parker Johnson'),
        (13, N'Reese Brown'),
        (14, N'Hayden Clark'),
        (15, N'Rowan Lewis'),
        (16, N'Sydney Walker'),
        (17, N'Emerson Hall'),
        (18, N'Finley Young'),
        (19, N'Dakota King'),
        (20, N'Sawyer Wright'),
        (21, N'Blair Green'),
        (22, N'Kendall Adams'),
        (23, N'Logan Baker'),
        (24, N'Micah Nelson'),
        (25, N'Harper Carter');

    DECLARE @DemoDonors TABLE
    (
        RowNumber INT NOT NULL PRIMARY KEY,
        EMADonorID INT NOT NULL UNIQUE
    );

    DECLARE @DonorNumber INT = 1;

    WHILE @DonorNumber <= 25
    BEGIN
        DECLARE @DonorName NVARCHAR(510) =
        (
            SELECT OfficialDonorName
            FROM @DonorNames
            WHERE RowNumber = @DonorNumber
        );

        INSERT INTO dbo.Donor
        (
            EMAOrganizationID,
            ChMSDonorID,
            EMADefaultCampusID,
            EmailAddress,
            GivingNumber,
            InteracDonorName,
            OfficialDonorName,
            Address,
            City,
            ProvinceStateCode,
            PostalCode,
            Country,
            PhoneNumber,
            IsDonorPilotingEtransfers,
            AcknowledgmentForManualTransactions,
            AcknowledgementForAutomatedTransactions,
            DateTimeCreated,
            DateTimeUpdated
        )
        VALUES
        (
            @DemoOrganizationID,
            CONCAT('DEMO-DONOR-', RIGHT('000' + CONVERT(VARCHAR(3), @DonorNumber), 3)),
            CASE WHEN @DonorNumber % 4 = 0 THEN @NorthCampusID ELSE @MainCampusID END,
            CONCAT('donor', RIGHT('00' + CONVERT(VARCHAR(2), @DonorNumber), 2), '@example.com'),
            CONCAT('D', RIGHT('0000' + CONVERT(VARCHAR(4), @DonorNumber), 4)),
            @DonorName,
            @DonorName,
            CONCAT(100 + @DonorNumber, ' Demo Street'),
            CASE
                WHEN @DonorNumber % 5 = 0 THEN 'Calgary'
                WHEN @DonorNumber % 7 = 0 THEN 'Red Deer'
                ELSE 'Edmonton'
            END,
            'AB',
            CONCAT('T5J ', @DonorNumber % 10, 'A', @DonorNumber % 10),
            'Canada',
            CONCAT('780555', RIGHT('0000' + CONVERT(VARCHAR(4), 1000 + @DonorNumber), 4)),
            'N',
            'Y',
            'Y',
            @Now,
            @Now
        );

        INSERT INTO @DemoDonors (RowNumber, EMADonorID)
        VALUES (@DonorNumber, CONVERT(INT, SCOPE_IDENTITY()));

        SET @DonorNumber += 1;
    END;

    /* ---------------------------------------------------------------------
       Organization defaults now point only at rows created above.
       --------------------------------------------------------------------- */

    UPDATE dbo.Organization
    SET
        EMADonorIDForAnonymous = @AnonymousDonorID,

        EMAEventIDForCreditDebitCards = @GeneralEventID,
        EMASourceIDForCreditDebitCards = @OnlineSourceID,
        EMAMethodOfGivingIDForCreditDebitCards = @CreditCardMethodID,
        EMAFundIDForCreditDebitCards = @GeneralFundID,
        EMACampusIDForCreditDebitCards = @MainCampusID,

        EMAEventIDForEtransfers = @GeneralEventID,
        EMASourceIDForEtransfers = @OnlineSourceID,
        EMAMethodOfGivingIDForETransfers = @EtransferMethodID,
        EMAFundIDForEtransfer = @GeneralFundID,
        EMACampusIDForEtransfers = @MainCampusID,

        EMAEventIDForPADs = @GeneralEventID,
        EMASourceIDForPADs = @OnlineSourceID,
        EMAMethodOfGivingIDForPADs = @PADMethodID,
        EMAFundIDForPADs = @GeneralFundID,
        EMACampusIDForPADs = @MainCampusID,

        EMAMethodOfGivingIDForCash = @CashMethodID,
        EMAMethodOfGivingIDForCheques = @ChequeMethodID,

        DateTimeUpdated = @Now
    WHERE EMAOrganizationID = @DemoOrganizationID;

    /* ---------------------------------------------------------------------
       100 completely new fictional donations across roughly two years.
       --------------------------------------------------------------------- */

    DECLARE @DonationNumber INT = 1;

    WHILE @DonationNumber <= 100
    BEGIN
        DECLARE
            @DonationDonorID INT,
            @DonationMethodID INT,
            @DonationSourceID INT,
            @DonationEventID INT,
            @DonationTributeTypeID INT,
            @DonationTributeText VARCHAR(255),
            @DonationFundID INT,
            @SecondaryFundID INT,
            @DonationCampusID INT,
            @DonationAmount MONEY,
            @DonationDate DATETIME2(7),
            @DonationID BIGINT,
            @PrimaryDistributionAmount MONEY,
            @SecondaryDistributionAmount MONEY;

        IF @DonationNumber % 20 = 0
            SET @DonationDonorID = @AnonymousDonorID;
        ELSE
            SELECT @DonationDonorID = EMADonorID
            FROM @DemoDonors
            WHERE RowNumber = ((@DonationNumber - 1) % 25) + 1;

        SET @DonationMethodID =
            CASE @DonationNumber % 10
                WHEN 0 THEN @PADMethodID
                WHEN 1 THEN @EtransferMethodID
                WHEN 2 THEN @EtransferMethodID
                WHEN 3 THEN @EtransferMethodID
                WHEN 4 THEN @EtransferMethodID
                WHEN 5 THEN @CreditCardMethodID
                WHEN 6 THEN @CreditCardMethodID
                WHEN 7 THEN @DebitCardMethodID
                WHEN 8 THEN @ChequeMethodID
                ELSE @CashMethodID
            END;

        SET @DonationSourceID =
            CASE @DonationNumber % 4
                WHEN 0 THEN @SpecialEventSourceID
                WHEN 1 THEN @OnlineSourceID
                WHEN 2 THEN @InPersonSourceID
                ELSE @MailSourceID
            END;

        SET @DonationEventID =
            CASE @DonationNumber % 5
                WHEN 0 THEN @GeneralEventID
                WHEN 1 THEN @SundayEventID
                WHEN 2 THEN @ChristmasEventID
                WHEN 3 THEN @YouthEventID
                ELSE @OutreachEventID
            END;

        SET @DonationTributeTypeID =
            CASE
                WHEN @DonationNumber % 17 = 0 THEN @InMemoryTributeID
                WHEN @DonationNumber % 13 = 0 THEN @InHonorTributeID
                WHEN @DonationNumber % 19 = 0 THEN @RecognitionTributeID
                ELSE NULL
            END;

        SET @DonationTributeText =
            CASE
                WHEN @DonationTributeTypeID IS NOT NULL
                    THEN CONCAT('Demo tribute ', @DonationNumber)
                ELSE NULL
            END;

        SELECT @DonationFundID = EMAFundID
        FROM @Funds
        WHERE RowNumber = ((@DonationNumber - 1) % 5) + 1;

        SELECT @SecondaryFundID = EMAFundID
        FROM @Funds
        WHERE RowNumber = (@DonationNumber % 5) + 1;

        SET @DonationCampusID =
            CASE WHEN @DonationNumber % 4 = 0 THEN @NorthCampusID ELSE @MainCampusID END;

        SET @DonationAmount =
            CASE @DonationNumber % 16
                WHEN 0 THEN 2500.00
                WHEN 1 THEN   25.00
                WHEN 2 THEN   50.00
                WHEN 3 THEN   75.00
                WHEN 4 THEN  100.00
                WHEN 5 THEN  125.00
                WHEN 6 THEN  150.00
                WHEN 7 THEN  200.00
                WHEN 8 THEN  250.00
                WHEN 9 THEN  300.00
                WHEN 10 THEN 500.00
                WHEN 11 THEN 750.00
                WHEN 12 THEN 1000.00
                WHEN 13 THEN 50.00
                WHEN 14 THEN 100.00
                ELSE 175.00
            END;

        SET @DonationDate =
            DATEADD
            (
                DAY,
                -CONVERT
                (
                    INT,
                    ROUND(
                        730.0 * (100 - @DonationNumber) / 99.0,
                        0
                    )
                ),
                @Now
            );

        INSERT INTO dbo.Donation
        (
            EMAOrganizationID,
            EMADonorID,
            ChMSDonationID,
            Comments,
            DateTimeReceived,
            DeadlineToMakeFundChanges,
            EMADonationSourceID,
            EMAEventID,
            EMAMethodOfGivingID,
            EMATributeTypeID,
            TributeText,
            InteracDonorName,
            ReferenceNumber,
            DonationAmount,
            IsTemplate,
            DateTimeCreated,
            DateTimeUpdated
        )
        VALUES
        (
            @DemoOrganizationID,
            @DonationDonorID,
            CONCAT('DEMO-DONATION-', RIGHT('000' + CONVERT(VARCHAR(3), @DonationNumber), 3)),
            CASE
                WHEN @DonationNumber % 10 = 0 THEN 'Recurring demo gift'
                WHEN @DonationNumber % 9 = 0 THEN 'Special campaign demo gift'
                ELSE 'EMA Demo Org donation'
            END,
            @DonationDate,
            DATEADD(HOUR, 48, @DonationDate),
            @DonationSourceID,
            @DonationEventID,
            @DonationMethodID,
            @DonationTributeTypeID,
            @DonationTributeText,
            CASE
                WHEN @DonationDonorID = @AnonymousDonorID THEN 'Anonymous Donor'
                ELSE
                (
                    SELECT CONVERT(VARCHAR(255), InteracDonorName)
                    FROM dbo.Donor
                    WHERE EMADonorID = @DonationDonorID
                      AND EMAOrganizationID = @DemoOrganizationID
                )
            END,
            CONCAT('EMA-DEMO-', @DemoOrganizationID, '-', RIGHT('000' + CONVERT(VARCHAR(3), @DonationNumber), 3)),
            @DonationAmount,
            'N',
            @DonationDate,
            @DonationDate
        );

        SET @DonationID = CONVERT(BIGINT, SCOPE_IDENTITY());

        IF @DonationNumber % 10 = 0
        BEGIN
            SET @PrimaryDistributionAmount = ROUND(@DonationAmount * 0.80, 2);
            SET @SecondaryDistributionAmount = @DonationAmount - @PrimaryDistributionAmount;

            INSERT INTO dbo.DonationDistribution
            (
                EMADonationID,
                EMACampusID,
                EMAFundID,
                DistributionAmount
            )
            VALUES
                (@DonationID, @DonationCampusID, @DonationFundID,   @PrimaryDistributionAmount),
                (@DonationID, @DonationCampusID, @SecondaryFundID, @SecondaryDistributionAmount);
        END
        ELSE
        BEGIN
            INSERT INTO dbo.DonationDistribution
            (
                EMADonationID,
                EMACampusID,
                EMAFundID,
                DistributionAmount
            )
            VALUES
            (
                @DonationID,
                @DonationCampusID,
                @DonationFundID,
                @DonationAmount
            );
        END;

        SET @DonationNumber += 1;
    END;

    /* ---------------------------------------------------------------------
       Validate the template before promotion.
       --------------------------------------------------------------------- */

    IF
    (
        SELECT COUNT(*)
        FROM dbo.Donor
        WHERE EMAOrganizationID = @DemoOrganizationID
    ) <> 26
        THROW 51109,
            'EMA Demo Org must contain exactly 26 donors (25 normal plus anonymous).',
            1;

    IF
    (
        SELECT COUNT(*)
        FROM dbo.Donation
        WHERE EMAOrganizationID = @DemoOrganizationID
    ) <> 100
        THROW 51110,
            'EMA Demo Org must contain exactly 100 donations.',
            1;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.Donation donation
        LEFT JOIN dbo.DonationDistribution distribution
            ON distribution.EMADonationID = donation.EMADonationID
        WHERE donation.EMAOrganizationID = @DemoOrganizationID
        GROUP BY donation.EMADonationID, donation.DonationAmount
        HAVING
            COUNT(distribution.EMADonationDistributionID) = 0
            OR ISNULL(SUM(distribution.DistributionAmount), 0)
               <> ISNULL(donation.DonationAmount, 0)
    )
        THROW 51111,
            'EMA Demo Org contains a donation with missing or mismatched distributions.',
            1;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.Donation donation
        LEFT JOIN dbo.Donor donor
            ON donor.EMADonorID = donation.EMADonorID
           AND donor.EMAOrganizationID = @DemoOrganizationID
        WHERE donation.EMAOrganizationID = @DemoOrganizationID
          AND donor.EMADonorID IS NULL
    )
        THROW 51112,
            'EMA Demo Org contains a donation whose donor does not belong to EMA Demo Org.',
            1;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.DonationDistribution distribution
        INNER JOIN dbo.Donation donation
            ON donation.EMADonationID = distribution.EMADonationID
        LEFT JOIN dbo.OrganizationFund fund
            ON fund.EMAFundID = distribution.EMAFundID
           AND fund.EMAOrganizationID = @DemoOrganizationID
        LEFT JOIN dbo.OrganizationCampus campus
            ON campus.EMACampusID = distribution.EMACampusID
           AND campus.EMAOrganizationID = @DemoOrganizationID
        WHERE donation.EMAOrganizationID = @DemoOrganizationID
          AND
          (
              fund.EMAFundID IS NULL
              OR
              (
                  distribution.EMACampusID IS NOT NULL
                  AND campus.EMACampusID IS NULL
              )
          )
    )
        THROW 51113,
            'EMA Demo Org contains a distribution that references another organization.',
            1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.OrganizationUser ou
        INNER JOIN dbo.[User] u
            ON u.UserID = ou.UserID
        INNER JOIN dbo.OrganizationUserRole ur
            ON ur.EMAOrganizationID = ou.EMAOrganizationID
           AND ur.UserID = ou.UserID
        WHERE ou.EMAOrganizationID = @DemoOrganizationID
          AND ou.UserID = @DemoUserID
          AND ou.OrganizationUserStatusID = @OrganizationUserStatusID
          AND ur.RoleID = @TemplateRoleID
          AND u.IsActive = 1
    )
        THROW 51114,
            'EMA Demo Org template admin membership or role was not created correctly.',
            1;

    /* ---------------------------------------------------------------------
       Promote only after the independent template is complete.
       --------------------------------------------------------------------- */

    UPDATE dbo.Organization
    SET
        OrganizationStatusID = @DemoStatusID,
        DateTimeUpdated = SYSUTCDATETIME()
    WHERE EMAOrganizationID = @DemoOrganizationID
      AND OrganizationStatusID = @SandboxStatusID;

    IF @@ROWCOUNT <> 1
        THROW 51115,
            'EMA Demo Org could not be promoted from sandbox status 0 to status 5000.',
            1;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;

SELECT
    @DemoOrganizationID AS SandboxTemplateOrganizationID,
    @DemoUserID AS SandboxTemplateUserID,
    organization.OrganizationName,
    organization.OrganizationStatusID,
    COUNT(DISTINCT donor.EMADonorID) AS TotalDonors,
    COUNT(DISTINCT donation.EMADonationID) AS TotalDonations,
    MIN(donation.DateTimeReceived) AS OldestDonation,
    MAX(donation.DateTimeReceived) AS NewestDonation
FROM dbo.Organization organization
LEFT JOIN dbo.Donor donor
    ON donor.EMAOrganizationID = organization.EMAOrganizationID
LEFT JOIN dbo.Donation donation
    ON donation.EMAOrganizationID = organization.EMAOrganizationID
WHERE organization.EMAOrganizationID = @DemoOrganizationID
GROUP BY
    organization.OrganizationName,
    organization.OrganizationStatusID;
GO
