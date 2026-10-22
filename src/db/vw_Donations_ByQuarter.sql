CREATE OR ALTER VIEW dbo.vw_Donations_ByQuarter
AS
SELECT
    EMAOrganizationID,
    YEAR(DateTimeReceived) AS DonationYear,
    DATEPART(QUARTER, DateTimeReceived) AS DonationQuarter,
    DATEFROMPARTS(
        YEAR(DateTimeReceived),
        (DATEPART(QUARTER, DateTimeReceived) - 1) * 3 + 1,
        1
    ) AS QuarterStartDate,
    SUM(DonationAmount) AS TotalDonations,
    COUNT(EMADonationID) AS DonationCount,
    AVG(DonationAmount) AS AverageGiftSize
FROM dbo.Donation
WHERE DateTimeReceived IS NOT NULL
GROUP BY
    EMAOrganizationID,
    YEAR(DateTimeReceived),
    DATEPART(QUARTER, DateTimeReceived);
GO
