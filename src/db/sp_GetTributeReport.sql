-- Task 29 adds EMATributeTypeID, DonationCount and TotalValue. The type ID lets each row drill
-- down to its donors (sp_GetTributeDonorsReport) without matching on the description text.
CREATE OR ALTER PROCEDURE [dbo].[sp_GetTributeReport]
    @OrganizationID INT,
    @StartDate DATE,
    @EndDate DATE
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        ott.EMATributeTypeID AS EMATributeTypeID,
        ott.Description AS [Type],
        don.TributeText AS [Comment],
        COUNT(*) AS DonationCount,
        SUM(don.DonationAmount) AS TotalValue
    FROM Donation don
    INNER JOIN OrganizationTributeType ott
        ON don.EMATributeTypeID = ott.EMATributeTypeID
       AND don.EMAOrganizationID = ott.EMAOrganizationID
    WHERE don.EMAOrganizationID = @OrganizationID
      AND CAST(don.DateTimeReceived AS DATE) BETWEEN @StartDate AND @EndDate
      AND don.EMATributeTypeID IS NOT NULL
    GROUP BY
        ott.EMATributeTypeID,
        ott.Description,
        don.TributeText
    ORDER BY
        [Type] ASC,
        [Comment] ASC;
END;
GO
