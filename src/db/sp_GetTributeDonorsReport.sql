-- Tribute Donors drill-down (Task 29). Returns the distinct donors behind one Tribute Report row:
-- the tribute type plus either a specific name or, when @HasName = 0, that type's unnamed tributes
-- (TributeText IS NULL). A NULL can't travel through a query string unambiguously, so the flag makes
-- "unnamed" an explicit filter rather than an accident of an empty parameter.
--
-- EXISTS yields one row per donor however many tribute gifts they gave. The donor must belong to the
-- organization as well as the donation, so contact details can never be read across tenants.
CREATE OR ALTER PROCEDURE dbo.sp_GetTributeDonorsReport
    @OrganizationID INT,
    @TributeTypeID  INT,
    @HasName        BIT,
    @TributeText    VARCHAR(255) = NULL,
    @StartDate      DATE,
    @EndDate        DATE
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        dn.EMADonorID,
        dn.OfficialDonorName,
        dn.EmailAddress,
        dn.PhoneNumber,
        dn.Address,
        dn.City,
        dn.ProvinceStateCode,
        dn.PostalCode
    FROM dbo.Donor dn
    WHERE dn.EMAOrganizationID = @OrganizationID
      AND EXISTS
      (
          SELECT 1
          FROM dbo.Donation don
          WHERE don.EMADonorID = dn.EMADonorID
            AND don.EMAOrganizationID = @OrganizationID
            AND don.EMATributeTypeID = @TributeTypeID
            AND CAST(don.DateTimeReceived AS DATE) BETWEEN @StartDate AND @EndDate
            AND (
                    (@HasName = 0 AND don.TributeText IS NULL)
                 OR (@HasName = 1 AND don.TributeText = @TributeText)
                )
      )
    ORDER BY
        dn.OfficialDonorName ASC,
        dn.EMADonorID ASC;
END;
GO
