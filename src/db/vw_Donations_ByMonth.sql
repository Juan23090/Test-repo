CREATE OR ALTER VIEW dbo.vw_Donations_ByMonth
AS
SELECT
    EMAOrganizationID,
    YEAR(DateTimeReceived) AS DonationYear,
    MONTH(DateTimeReceived) AS DonationMonth,
    DATEFROMPARTS(
        YEAR(DateTimeReceived),
        MONTH(DateTimeReceived),
        1
    ) AS MonthStartDate,
    SUM(DonationAmount) AS TotalDonations,
    COUNT(EMADonationID) AS DonationCount,
    AVG(DonationAmount) AS AverageGiftSize
FROM dbo.Donation
WHERE DateTimeReceived IS NOT NULL
GROUP BY
    EMAOrganizationID,
    YEAR(DateTimeReceived),
    MONTH(DateTimeReceived);
GO
