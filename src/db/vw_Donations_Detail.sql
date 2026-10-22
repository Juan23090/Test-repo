CREATE OR ALTER VIEW dbo.vw_Donations_Detail
AS
SELECT
    donation.EMADonationID,
    donation.EMAOrganizationID,
    donation.EMADonorID,
    donation.InteracDonorName,
    donation.DonationAmount,
    donation.DateTimeReceived,
    donation.DateTimeCreated,
    donation.EMACurrencyTypeID,
    donation.EMAMethodOfGivingID,
    method.Description AS MethodName,
    fund.EMAFundID,
    fund.FundName
FROM dbo.Donation AS donation
LEFT JOIN dbo.OrganizationMethodOfGiving AS method
    ON method.EMAMethodOfGivingID =
       donation.EMAMethodOfGivingID
   AND method.EMAOrganizationID =
       donation.EMAOrganizationID
OUTER APPLY
(
    SELECT TOP (1)
        distribution.EMAFundID,
        organizationFund.Name AS FundName
    FROM dbo.DonationDistribution AS distribution
    LEFT JOIN dbo.OrganizationFund AS organizationFund
        ON organizationFund.EMAFundID =
           distribution.EMAFundID
       AND organizationFund.EMAOrganizationID =
           donation.EMAOrganizationID
    WHERE distribution.EMADonationID =
          donation.EMADonationID
    ORDER BY distribution.EMAFundID
) AS fund
WHERE donation.DateTimeReceived IS NOT NULL;
GO
