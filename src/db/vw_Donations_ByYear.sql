CREATE OR ALTER VIEW dbo.vw_Donations_ByYear
AS
SELECT
    EMAOrganizationID,
    YEAR(DateTimeReceived) AS DonationYear,
    SUM(DonationAmount) AS TotalDonations,
    COUNT(EMADonationID) AS DonationCount,
    AVG(DonationAmount) AS AverageGiftSize
FROM dbo.Donation
WHERE DateTimeReceived IS NOT NULL
GROUP BY
    EMAOrganizationID,
    YEAR(DateTimeReceived);
GO
