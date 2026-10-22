-- ============================================================================
-- Gift Report views â€” one per classification. Run against emasqldatabase-dev.
-- Daily granularity (PeriodDate = the donation date); the GetGiftReport API buckets
-- these into Day/Week/Month/Quarter/Year and pivots into the cross-tab.
--
-- Each view returns: OrganizationID, PeriodDate (DATE), ClassificationName (nvarchar),
--   TotalDonations (money), DonationCount (int), AverageGiftSize (money).
--
-- Schema notes (confirmed via INFORMATION_SCHEMA):
--   Donation.EMAOrganizationID (int); Donation.DateTimeReceived (datetime2);
--   amounts are money. DonationDistribution has no org column (org via JOIN to Donation).
--   All six lookups (incl. OrganizationFund) store IsActive as CHAR(1) 'Y'/'N' (= 'Y').
--   (OrganizationFund.IsActive was originally believed to be BIT, but a runtime conversion
--    error proved it is CHAR 'Y'/'N' like the rest.)
-- ============================================================================

CREATE OR ALTER VIEW vw_GiftReport_ByFund AS
SELECT
    d.EMAOrganizationID                       AS OrganizationID,
    CAST(d.DateTimeReceived AS DATE)       AS PeriodDate,
    CAST(f.Name AS NVARCHAR(255))          AS ClassificationName,
    SUM(dd.DistributionAmount)             AS TotalDonations,
    COUNT(*)                               AS DonationCount,
    AVG(dd.DistributionAmount)             AS AverageGiftSize
FROM DonationDistribution dd
INNER JOIN Donation d        ON dd.EMADonationID = d.EMADonationID
INNER JOIN OrganizationFund f ON dd.EMAFundID    = f.EMAFundID
WHERE f.IsActive = 'Y'
GROUP BY d.EMAOrganizationID, CAST(d.DateTimeReceived AS DATE), f.Name;
GO

CREATE OR ALTER VIEW vw_GiftReport_ByMethod AS
SELECT
    d.EMAOrganizationID                       AS OrganizationID,
    CAST(d.DateTimeReceived AS DATE)       AS PeriodDate,
    CAST(m.Description AS NVARCHAR(255))    AS ClassificationName,
    SUM(d.DonationAmount)                  AS TotalDonations,
    COUNT(*)                               AS DonationCount,
    AVG(d.DonationAmount)                  AS AverageGiftSize
FROM Donation d
INNER JOIN OrganizationMethodOfGiving m ON d.EMAMethodOfGivingID = m.EMAMethodOfGivingID
WHERE m.IsActive = 'Y'
GROUP BY d.EMAOrganizationID, CAST(d.DateTimeReceived AS DATE), m.Description;
GO

CREATE OR ALTER VIEW vw_GiftReport_ByCampus AS
SELECT
    d.EMAOrganizationID                       AS OrganizationID,
    CAST(d.DateTimeReceived AS DATE)       AS PeriodDate,
    CAST(c.Name AS NVARCHAR(255))          AS ClassificationName,
    SUM(dd.DistributionAmount)             AS TotalDonations,
    COUNT(*)                               AS DonationCount,
    AVG(dd.DistributionAmount)             AS AverageGiftSize
FROM DonationDistribution dd
INNER JOIN Donation d          ON dd.EMADonationID = d.EMADonationID
INNER JOIN OrganizationCampus c ON dd.EMACampusID  = c.EMACampusID
WHERE c.IsActive = 'Y'
GROUP BY d.EMAOrganizationID, CAST(d.DateTimeReceived AS DATE), c.Name;
GO

CREATE OR ALTER VIEW vw_GiftReport_ByEvent AS
SELECT
    d.EMAOrganizationID                       AS OrganizationID,
    CAST(d.DateTimeReceived AS DATE)       AS PeriodDate,
    CAST(e.Description AS NVARCHAR(255))    AS ClassificationName,
    SUM(d.DonationAmount)                  AS TotalDonations,
    COUNT(*)                               AS DonationCount,
    AVG(d.DonationAmount)                  AS AverageGiftSize
FROM Donation d
INNER JOIN OrganizationEvent e ON d.EMAEventID = e.EMAEventID
WHERE e.IsActive = 'Y'
GROUP BY d.EMAOrganizationID, CAST(d.DateTimeReceived AS DATE), e.Description;
GO

CREATE OR ALTER VIEW vw_GiftReport_BySource AS
SELECT
    d.EMAOrganizationID                       AS OrganizationID,
    CAST(d.DateTimeReceived AS DATE)       AS PeriodDate,
    CAST(s.Description AS NVARCHAR(255))    AS ClassificationName,
    SUM(d.DonationAmount)                  AS TotalDonations,
    COUNT(*)                               AS DonationCount,
    AVG(d.DonationAmount)                  AS AverageGiftSize
FROM Donation d
INNER JOIN OrganizationDonationSource s ON d.EMADonationSourceID = s.EMADonationSourceID
WHERE s.IsActive = 'Y'
GROUP BY d.EMAOrganizationID, CAST(d.DateTimeReceived AS DATE), s.Description;
GO

CREATE OR ALTER VIEW vw_GiftReport_ByTributeType AS
SELECT
    d.EMAOrganizationID                       AS OrganizationID,
    CAST(d.DateTimeReceived AS DATE)       AS PeriodDate,
    CAST(t.Description AS NVARCHAR(255))    AS ClassificationName,
    SUM(d.DonationAmount)                  AS TotalDonations,
    COUNT(*)                               AS DonationCount,
    AVG(d.DonationAmount)                  AS AverageGiftSize
FROM Donation d
INNER JOIN OrganizationTributeType t ON d.EMATributeTypeID = t.EMATributeTypeID
WHERE t.IsActive = 'Y'
GROUP BY d.EMAOrganizationID, CAST(d.DateTimeReceived AS DATE), t.Description;
GO

