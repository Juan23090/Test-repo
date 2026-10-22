/*
    Task 43 - Organization Journey status diagnostic.

    READ ONLY.
    This script does not INSERT, UPDATE, DELETE, ALTER, or otherwise mutate data.

    The Task 39.1 screen-feedback transaction must create a linked
    dbo.OrganizationJourney row. OrganizationJourney.OrganizationStatusID is
    required and references dbo.OrganizationStatus. The application resolves it as:
      1. the organization's current status, if that status exists in the lookup;
      2. otherwise the latest valid Journey status for that organization;
      3. otherwise it throws SQL error 51039 rather than inventing a status.
*/
SET NOCOUNT ON;

DECLARE @OrganizationID INT = 25; -- Test Org Abdullah in current dev testing; change for another org if needed.

SELECT
    o.EMAOrganizationID,
    o.OrganizationName,
    o.OrganizationStatusID AS CurrentOrganizationStatusID,
    currentStatus.Description AS CurrentStatusDescription,
    CASE
        WHEN currentStatus.OrganizationStatusID IS NOT NULL THEN 'VALID'
        WHEN o.OrganizationStatusID IS NULL THEN 'NULL / NOT CONFIGURED'
        ELSE 'INVALID / LOOKUP ROW MISSING'
    END AS CurrentStatusState
FROM dbo.Organization AS o
LEFT JOIN dbo.OrganizationStatus AS currentStatus
    ON currentStatus.OrganizationStatusID =
       TRY_CONVERT(SMALLINT, o.OrganizationStatusID)
WHERE o.EMAOrganizationID = @OrganizationID;

SELECT
    s.OrganizationStatusID,
    s.Sequence,
    s.Description
FROM dbo.OrganizationStatus AS s
ORDER BY s.Sequence, s.OrganizationStatusID;

SELECT TOP (20)
    j.OrganizationInteractionID,
    j.ActivityDateTime,
    j.ActivityTypeID,
    j.OrganizationStatusID,
    s.Description AS OrganizationStatusDescription,
    j.MenuItemID,
    j.FAQID,
    j.EnhancementRequestID,
    j.SupportTicketID
FROM dbo.OrganizationJourney AS j
LEFT JOIN dbo.OrganizationStatus AS s
    ON s.OrganizationStatusID = j.OrganizationStatusID
WHERE j.EMAOrganizationID = @OrganizationID
ORDER BY
    j.ActivityDateTime DESC,
    j.OrganizationInteractionID DESC;

DECLARE @ResolvedStatusID SMALLINT;

SELECT
    @ResolvedStatusID = s.OrganizationStatusID
FROM dbo.Organization AS o
INNER JOIN dbo.OrganizationStatus AS s
    ON s.OrganizationStatusID =
       TRY_CONVERT(SMALLINT, o.OrganizationStatusID)
WHERE o.EMAOrganizationID = @OrganizationID;

IF @ResolvedStatusID IS NULL
BEGIN
    SELECT TOP (1)
        @ResolvedStatusID = j.OrganizationStatusID
    FROM dbo.OrganizationJourney AS j
    INNER JOIN dbo.OrganizationStatus AS s
        ON s.OrganizationStatusID = j.OrganizationStatusID
    WHERE j.EMAOrganizationID = @OrganizationID
    ORDER BY
        j.ActivityDateTime DESC,
        j.OrganizationInteractionID DESC;
END;

SELECT
    @OrganizationID AS EMAOrganizationID,
    @ResolvedStatusID AS StatusThatTask39Point1WouldUse,
    CASE
        WHEN @ResolvedStatusID IS NULL
            THEN 'FAILS: Create FAQ/Enhancement/Support throws 51039 until a valid organization status is configured.'
        ELSE 'PASS: Task 39.1 has a valid status for the linked Journey row.'
    END AS Task43JourneyStatusResult;

/* Show every organization that would currently hit the same 51039 path. */
SELECT
    o.EMAOrganizationID,
    o.OrganizationName,
    o.OrganizationStatusID AS CurrentOrganizationStatusID
FROM dbo.Organization AS o
OUTER APPLY
(
    SELECT TOP (1)
        s.OrganizationStatusID
    FROM dbo.OrganizationStatus AS s
    WHERE s.OrganizationStatusID =
          TRY_CONVERT(SMALLINT, o.OrganizationStatusID)
) AS currentValid
OUTER APPLY
(
    SELECT TOP (1)
        j.OrganizationStatusID
    FROM dbo.OrganizationJourney AS j
    INNER JOIN dbo.OrganizationStatus AS s
        ON s.OrganizationStatusID = j.OrganizationStatusID
    WHERE j.EMAOrganizationID = o.EMAOrganizationID
    ORDER BY
        j.ActivityDateTime DESC,
        j.OrganizationInteractionID DESC
) AS historyValid
WHERE currentValid.OrganizationStatusID IS NULL
  AND historyValid.OrganizationStatusID IS NULL
ORDER BY o.EMAOrganizationID;
