/*
    Task 43 - Screen Action / Journey status verifier
    READ ONLY. No INSERT / UPDATE / DELETE / MERGE / DDL.
*/
SET NOCOUNT ON;

;WITH LatestValidJourneyStatus AS
(
    SELECT
        oj.EMAOrganizationID,
        oj.OrganizationStatusID,
        oj.ActivityDateTime,
        oj.OrganizationInteractionID,
        ROW_NUMBER() OVER
        (
            PARTITION BY oj.EMAOrganizationID
            ORDER BY oj.ActivityDateTime DESC, oj.OrganizationInteractionID DESC
        ) AS rn
    FROM dbo.OrganizationJourney AS oj
    INNER JOIN dbo.OrganizationStatus AS os
        ON os.OrganizationStatusID = oj.OrganizationStatusID
)
SELECT
    o.EMAOrganizationID,
    o.OrganizationName,
    o.OrganizationStatusID AS CurrentOrganizationStatusID,
    currentStatus.Description AS CurrentStatusDescription,
    latest.OrganizationStatusID AS LatestValidJourneyStatusID,
    latest.ActivityDateTime AS LatestValidJourneyStatusDateTime,
    CASE
        WHEN currentStatus.OrganizationStatusID IS NOT NULL THEN 'CURRENT STATUS IS VALID'
        WHEN latest.OrganizationStatusID IS NOT NULL THEN 'WILL USE JOURNEY FALLBACK'
        ELSE 'WILL THROW 51039 - NO VALID STATUS AVAILABLE'
    END AS Task43ExpectedBehavior
FROM dbo.Organization AS o
LEFT JOIN dbo.OrganizationStatus AS currentStatus
    ON currentStatus.OrganizationStatusID = TRY_CONVERT(SMALLINT, o.OrganizationStatusID)
LEFT JOIN LatestValidJourneyStatus AS latest
    ON latest.EMAOrganizationID = o.EMAOrganizationID
   AND latest.rn = 1
ORDER BY o.EMAOrganizationID;

SELECT
    os.OrganizationStatusID,
    os.Description AS OrganizationStatusDescription
FROM dbo.OrganizationStatus AS os
ORDER BY os.OrganizationStatusID;

SELECT TOP (100)
    oj.EMAOrganizationID,
    oj.OrganizationInteractionID,
    oj.ActivityDateTime,
    oj.OrganizationStatusID,
    os.Description AS OrganizationStatusDescription,
    oj.ActivityTypeID,
    oj.InteractionComments
FROM dbo.OrganizationJourney AS oj
LEFT JOIN dbo.OrganizationStatus AS os
    ON os.OrganizationStatusID = oj.OrganizationStatusID
ORDER BY oj.ActivityDateTime DESC, oj.OrganizationInteractionID DESC;
