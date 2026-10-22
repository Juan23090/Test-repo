CREATE OR ALTER VIEW dbo.vw_Donations_ByWeek
AS
WITH DonationWeeks AS
(
    SELECT
        EMAOrganizationID,
        EMADonationID,
        DonationAmount,
        DATEADD(
            DAY,
            -(
                DATEDIFF(
                    DAY,
                    CONVERT(DATE, '19000101', 112),
                    CAST(DateTimeReceived AS DATE)
                ) % 7
            ),
            CAST(DateTimeReceived AS DATE)
        ) AS WeekStartDate
    FROM dbo.Donation
    WHERE DateTimeReceived IS NOT NULL
)
SELECT
    EMAOrganizationID,
    YEAR(DATEADD(DAY, 3, WeekStartDate)) AS DonationYear,
    DATEPART(ISO_WEEK, WeekStartDate) AS DonationWeek,
    WeekStartDate,
    SUM(DonationAmount) AS TotalDonations,
    COUNT(EMADonationID) AS DonationCount,
    AVG(DonationAmount) AS AverageGiftSize
FROM DonationWeeks
GROUP BY
    EMAOrganizationID,
    YEAR(DATEADD(DAY, 3, WeekStartDate)),
    DATEPART(ISO_WEEK, WeekStartDate),
    WeekStartDate;
GO
