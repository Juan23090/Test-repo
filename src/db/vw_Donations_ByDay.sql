CREATE OR ALTER VIEW dbo.vw_Donations_ByDay
AS
SELECT
    EMAOrganizationID,
    CAST(DateTimeReceived AS DATE) AS DonationDate,
    SUM(DonationAmount) AS TotalDonations,
    COUNT(EMADonationID) AS DonationCount,
    AVG(DonationAmount) AS AverageGiftSize
FROM dbo.Donation
WHERE DateTimeReceived IS NOT NULL
GROUP BY
    EMAOrganizationID,
    CAST(DateTimeReceived AS DATE);
GO
