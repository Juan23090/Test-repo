-- EMA Seed Data Script
-- Environment: Development / Sandbox
-- Volume: 1/10th of production target
-- Dependency order: CurrencyType, MethodOfGiving, Campus, Fund, Organization, Donor, Donation, DonationDistribution
-- NOTE: Sections 1-4 are minimal reference data to satisfy FK constraints.
-- Roger will replace these with full lookup configuration once SQL Server is ready.

-- 1. CURRENCY TYPES
INSERT INTO OrganizationCurrencyType (EMAOrganizationID, ChMSCurrencyTypeID, Description, IsActive, DateTimeCreated, CreatedByUserId, DateTimeUpdated, UpdatedByUserId)
VALUES
(1, 1, 'Canadian Dollar', 1, GETUTCDATE(), 'seed', GETUTCDATE(), 'seed'),
(1, 2, 'US Dollar',       1, GETUTCDATE(), 'seed', GETUTCDATE(), 'seed'),
(1, 3, 'Euro',            0, GETUTCDATE(), 'seed', GETUTCDATE(), 'seed');

-- 2. METHODS OF GIVING
INSERT INTO OrganizationMethodOfGiving (EMAOrganizationID, ChMSMethodOfGivingID, Description, BillingRateGroup, CRAReceiptable, AcknowledgeToDonor, AcknowledgeToOrganization, AcknowledgeToEMASupport, IsActive, IsDefault, DateTimeCreated, CreatedByUserId, DateTimeUpdated, UpdatedByUserId)
VALUES
(1, 1, 'e-Transfer',    'A', 1, 1, 1, 0, 1, 1, GETUTCDATE(), 'seed', GETUTCDATE(), 'seed'),
(1, 2, 'Credit Card',   'B', 1, 1, 1, 0, 1, 0, GETUTCDATE(), 'seed', GETUTCDATE(), 'seed'),
(1, 3, 'Cheque',        'A', 1, 1, 0, 0, 1, 0, GETUTCDATE(), 'seed', GETUTCDATE(), 'seed'),
(1, 4, 'Cash',          'A', 1, 0, 0, 0, 1, 0, GETUTCDATE(), 'seed', GETUTCDATE(), 'seed'),
(1, 5, 'Bank Transfer', 'B', 1, 1, 1, 0, 0, 0, GETUTCDATE(), 'seed', GETUTCDATE(), 'seed');

-- 3. CAMPUSES
INSERT INTO OrganizationCampus (EMAOrganizationID, ChMSCampusID, CampusName, DefaultCampusFundId, CampusActive, IsDefault, DateTimeCreated, CreatedByUserId, DateTimeUpdated, UpdatedByUserId)
VALUES
(1, 1, 'Main Campus',  0, 1, 1, GETUTCDATE(), 'seed', GETUTCDATE(), 'seed'),
(1, 2, 'North Campus', 0, 1, 0, GETUTCDATE(), 'seed', GETUTCDATE(), 'seed'),
(1, 3, 'South Campus', 0, 1, 0, GETUTCDATE(), 'seed', GETUTCDATE(), 'seed'),
(1, 4, 'East Campus',  0, 0, 0, GETUTCDATE(), 'seed', GETUTCDATE(), 'seed');

-- 4. FUNDS
INSERT INTO OrganizationFund (EMAOrganizationID, ChMSFundID, FundName, TaxDeductible, EMACampusID, FundActive, IsDefault, DateTimeCreated, CreatedByUserId, DateTimeUpdated, UpdatedByUserId)
VALUES
(1, 1, 'General Fund',       1, 1, 1, 1, GETUTCDATE(), 'seed', GETUTCDATE(), 'seed'),
(1, 2, 'Building Fund',      1, 1, 1, 0, GETUTCDATE(), 'seed', GETUTCDATE(), 'seed'),
(1, 3, 'Missions Fund',      1, 2, 1, 0, GETUTCDATE(), 'seed', GETUTCDATE(), 'seed'),
(1, 4, 'Youth Fund',         1, 2, 1, 0, GETUTCDATE(), 'seed', GETUTCDATE(), 'seed'),
(1, 5, 'Community Outreach', 1, 3, 1, 0, GETUTCDATE(), 'seed', GETUTCDATE(), 'seed'),
(1, 6, 'Benevolence Fund',   0, 3, 0, 0, GETUTCDATE(), 'seed', GETUTCDATE(), 'seed');

-- 5. ORGANIZATIONS (100 rows)
DECLARE @i INT = 1;
WHILE @i <= 100
BEGIN
    INSERT INTO Organization (
        BillingOrganizationGroupID, BillingBeginDateTime, ChMSID, OrganizationName,
        AdminEmailAddress, PhoneNumber, EtransferEmailAddress, ChMSUserID,
        EMAFundIDDefault, EMAMethodOfGivingIDForeTransfers, OrganizationWebSite,
        Address, City, ProvinceStateCode, PostalCode, CRARegisteredCharityNumber,
        OrganizationStatusID, DateTimeCreated, CreatedByUserId, DateTimeUpdated, UpdatedByUserId
    )
    VALUES (
        1,
        GETUTCDATE(),
        @i,
        CONCAT('Church Organization ', @i),
        CONCAT('admin', @i, '@church', @i, '.ca'),
        CONCAT('403-555-', FORMAT(@i, '0000')),
        CONCAT('etransfer', @i, '@church', @i, '.ca'),
        CONCAT('chms-user-', @i),
        1, 1,
        CONCAT('https://church', @i, '.ca'),
        CONCAT(@i, ' Main Street'),
        CASE (@i % 5)
            WHEN 0 THEN 'Calgary'
            WHEN 1 THEN 'Edmonton'
            WHEN 2 THEN 'Vancouver'
            WHEN 3 THEN 'Toronto'
            ELSE 'Winnipeg'
        END,
        CASE (@i % 5)
            WHEN 0 THEN 'AB'
            WHEN 1 THEN 'AB'
            WHEN 2 THEN 'BC'
            WHEN 3 THEN 'ON'
            ELSE 'MB'
        END,
        CONCAT('T', FORMAT(@i, '0000'), 'AA'),
        CONCAT('RR', FORMAT(@i, '000000')),
        1,
        GETUTCDATE(), 'seed', GETUTCDATE(), 'seed'
    );
    SET @i = @i + 1;
END;

-- 6. DONORS (1000 rows)
SET @i = 1;
WHILE @i <= 1000
BEGIN
    INSERT INTO Donor (
        OrganizationID, ChMSDonorID, EMACampusID, EmailAddress, Name,
        Address, City, ProvinceStateCode, PostalCode, IsActive,
        DateTimeCreated, CreatedByUserId, DateTimeUpdated, UpdatedByUserId
    )
    VALUES (
        (@i % 100) + 1,
        @i,
        (@i % 3) + 1,
        CONCAT('donor', @i, '@email.com'),
        CONCAT(
            CASE (@i % 10)
                WHEN 0 THEN 'James'   WHEN 1 THEN 'Sarah'   WHEN 2 THEN 'Michael'
                WHEN 3 THEN 'Emily'   WHEN 4 THEN 'David'   WHEN 5 THEN 'Jessica'
                WHEN 6 THEN 'Daniel'  WHEN 7 THEN 'Ashley'  WHEN 8 THEN 'Matthew'
                ELSE 'Amanda'
            END,
            ' ',
            CASE (@i % 8)
                WHEN 0 THEN 'Smith'    WHEN 1 THEN 'Johnson'  WHEN 2 THEN 'Williams'
                WHEN 3 THEN 'Brown'    WHEN 4 THEN 'Jones'    WHEN 5 THEN 'Garcia'
                WHEN 6 THEN 'Miller'   ELSE 'Davis'
            END
        ),
        CONCAT(@i, ' Donor Street'),
        CASE (@i % 4)
            WHEN 0 THEN 'Calgary'   WHEN 1 THEN 'Edmonton'
            WHEN 2 THEN 'Vancouver' ELSE 'Toronto'
        END,
        CASE (@i % 4)
            WHEN 0 THEN 'AB' WHEN 1 THEN 'AB'
            WHEN 2 THEN 'BC' ELSE 'ON'
        END,
        CONCAT('T', FORMAT(@i, '0000'), 'BB'),
        CASE WHEN @i % 10 = 0 THEN 0 ELSE 1 END,
        GETUTCDATE(), 'seed', GETUTCDATE(), 'seed'
    );
    SET @i = @i + 1;
END;

-- 7. DONATIONS (10,000 rows)
SET @i = 1;
WHILE @i <= 10000
BEGIN
    INSERT INTO Donation (
        EMADonorID, EmailID, DateDonated, Comments,
        EMACurrencyTypeID, EMAMethodOfGivingID, ReferenceNumber,
        TotalDonationAmount, IsDefault, RandomNumber2FA,
        DateTimeCreated, CreatedByUserId, DateTimeUpdated, UpdatedByUserId
    )
    VALUES (
        (@i % 1000) + 1,
        NULL,
        DATEADD(DAY, -(@i % 365), GETUTCDATE()),
        CASE WHEN @i % 5 = 0 THEN 'Thank you for your generosity.' ELSE NULL END,
        (@i % 2) + 1,
        (@i % 4) + 1,
        CONCAT('REF-', FORMAT(@i, '00000')),
        CAST((@i % 500) * 1.5 + 25 AS DECIMAL(10,2)),
        0,
        CAST(RAND(CHECKSUM(NEWID())) * 9000 + 1000 AS INT),
        GETUTCDATE(), 'seed', GETUTCDATE(), 'seed'
    );
    SET @i = @i + 1;
END;

-- 8. DONATION DISTRIBUTIONS (12,000 rows)
SET @i = 1;
WHILE @i <= 12000
BEGIN
    INSERT INTO DonationDistribution (
        EMADonationID, EMACampusID, EMAFundID, DistributionAmount
    )
    VALUES (
        (@i % 10000) + 1,
        (@i % 3) + 1,
        (@i % 5) + 1,
        CAST((@i % 500) * 1.5 + 25 AS DECIMAL(10,2))
    );
    SET @i = @i + 1;
END;
