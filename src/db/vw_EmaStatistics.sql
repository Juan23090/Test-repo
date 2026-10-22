-- ============================================================================
-- EMA Statistics view â€” donation-grain rows across ALL organizations, with the
-- dimensions the GetEmaStatistics endpoint needs to compute every
-- Classification x Scope x Measurement combination in memory.
--
-- One row per donation:
--   OrganizationID, OrganizationName, ChMSID, ChMSName, EMADonorID,
--   PeriodDate (the donation date), DonationAmount,
--   IsEtransfer (1 when the donation used the org's designated eTransfer method).
--
-- Schema notes (confirmed via INFORMATION_SCHEMA on emasqldatabase-dev):
--   Donation.EMAOrganizationID (int); Donation.DateTimeReceived (datetime2);
--   eTransfer method designation = Organization.EMAMethodOfGivingIDForETransfers;
--   dbo.ChMS(ChMSID int, ChMSName varchar); org -> ChMS via Organization.ChMSID.
--   OrganizationID = 0 ("EMA Emails Not imported as Donations") is a staging bucket â€” excluded.
-- ============================================================================

CREATE OR ALTER VIEW vw_EmaStatistics AS
SELECT
    d.EMAOrganizationID                                       AS OrganizationID,
    o.OrganizationName                                     AS OrganizationName,
    o.ChMSID                                               AS ChMSID,
    c.ChMSName                                             AS ChMSName,
    d.EMADonorID                                           AS EMADonorID,
    CAST(d.DateTimeReceived AS DATE)                       AS PeriodDate,
    d.DonationAmount                                       AS DonationAmount,
    CASE
        WHEN o.EMAMethodOfGivingIDForETransfers IS NOT NULL
         AND d.EMAMethodOfGivingID = o.EMAMethodOfGivingIDForETransfers
        THEN 1 ELSE 0
    END                                                    AS IsEtransfer
FROM Donation d
INNER JOIN Organization o ON o.EMAOrganizationID = d.EMAOrganizationID
LEFT  JOIN ChMS c         ON c.ChMSID            = o.ChMSID
WHERE d.EMAOrganizationID > 0            -- exclude the staging "not imported" bucket (org 0)
  AND d.DateTimeReceived IS NOT NULL;
GO

