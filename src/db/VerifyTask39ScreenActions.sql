/*
    Task 39 - FAQ, Enhancement and Support Ticket contract verification.

    READ ONLY: this script does not insert, update, delete or alter anything.
    It throws when the database does not match the Task 39 code contract.
*/
SET NOCOUNT ON;

DECLARE @Missing TABLE
(
    Requirement nvarchar(400) NOT NULL
);

/* Required tables. */
INSERT INTO @Missing (Requirement)
SELECT required.Requirement
FROM
(
    VALUES
        (N'dbo.FrequentlyAskedQuestions'),
        (N'dbo.EnhancementRequest'),
        (N'dbo.EnhancementRequestStatus'),
        (N'dbo.EnhancementRequestVote'),
        (N'dbo.SupportTicket'),
        (N'dbo.SupportTicketStatus'),
        (N'dbo.OrganizationJourney'),
        (N'dbo.OrganizationJourneyActivityType')
) AS required(Requirement)
WHERE OBJECT_ID(required.Requirement, N'U') IS NULL;

/* Required columns used by the implementation. */
DECLARE @RequiredColumns TABLE
(
    TableName sysname NOT NULL,
    ColumnName sysname NOT NULL
);

INSERT INTO @RequiredColumns (TableName, ColumnName)
VALUES
    ('FrequentlyAskedQuestions', 'MenuItemID'),
    ('FrequentlyAskedQuestions', 'EMAOrganizationID'),
    ('FrequentlyAskedQuestions', 'FAQID'),
    ('FrequentlyAskedQuestions', 'Question'),
    ('FrequentlyAskedQuestions', 'Answer'),
    ('FrequentlyAskedQuestions', 'IsReviewed'),
    ('FrequentlyAskedQuestions', 'Comments'),
    ('FrequentlyAskedQuestions', 'IsActive'),
    ('FrequentlyAskedQuestions', 'DateTimeEntered'),
    ('FrequentlyAskedQuestions', 'AskedByUserID'),
    ('FrequentlyAskedQuestions', 'DateTimeUpdated'),
    ('FrequentlyAskedQuestions', 'UpdatedByUserID'),
    ('FrequentlyAskedQuestions', 'OrganizationInteractionID'),
    ('EnhancementRequest', 'MenuItemID'),
    ('EnhancementRequest', 'EMAOrganizationID'),
    ('EnhancementRequest', 'EnhancementRequestID'),
    ('EnhancementRequest', 'Title'),
    ('EnhancementRequest', 'Description'),
    ('EnhancementRequest', 'EMAComments'),
    ('EnhancementRequest', 'EnhancementRequestStatusID'),
    ('EnhancementRequest', 'DateTimeEntered'),
    ('EnhancementRequest', 'EnteredByUserID'),
    ('EnhancementRequest', 'DateTimeUpdated'),
    ('EnhancementRequest', 'UpdatedByUserID'),
    ('EnhancementRequest', 'OrganizationInteractionID'),
    ('EnhancementRequestStatus', 'EnhancementRequestStatusID'),
    ('EnhancementRequestStatus', 'Description'),
    ('EnhancementRequestStatus', 'ShowIcon'),
    ('EnhancementRequestStatus', 'IconColor'),
    ('EnhancementRequestStatus', 'MaxDaysAtStatus'),
    ('EnhancementRequestVote', 'OrganizationID'),
    ('EnhancementRequestVote', 'EnhancementRequestID'),
    ('EnhancementRequestVote', 'Vote'),
    ('EnhancementRequestVote', 'CreatedByUserID'),
    ('EnhancementRequestVote', 'UpdatedByUserID'),
    ('SupportTicket', 'MenuItemID'),
    ('SupportTicket', 'EMAOrganizationID'),
    ('SupportTicket', 'SupportTicketID'),
    ('SupportTicket', 'Title'),
    ('SupportTicket', 'Description'),
    ('SupportTicket', 'EMAComments'),
    ('SupportTicket', 'SupportTicketStatusID'),
    ('SupportTicket', 'DateTimeEntered'),
    ('SupportTicket', 'RequestedByUserID'),
    ('SupportTicket', 'DateTimeUpdated'),
    ('SupportTicket', 'UpdatedByEMAUserID'),
    ('SupportTicket', 'OrganizationInteractionID'),
    ('SupportTicketStatus', 'SupportTicketStatusID'),
    ('SupportTicketStatus', 'Description'),
    ('OrganizationJourney', 'OrganizationInteractionID'),
    ('OrganizationJourney', 'EMAOrganizationID'),
    ('OrganizationJourney', 'EMAUserID'),
    ('OrganizationJourney', 'MenuItemID'),
    ('OrganizationJourney', 'ActivityDateTime'),
    ('OrganizationJourney', 'ActivityTypeID'),
    ('OrganizationJourney', 'EnhancementRequestID'),
    ('OrganizationJourney', 'FAQID'),
    ('OrganizationJourney', 'SupportTicketID');

INSERT INTO @Missing (Requirement)
SELECT CONCAT(N'dbo.', required.TableName, N'.', required.ColumnName)
FROM @RequiredColumns AS required
WHERE COL_LENGTH(
          CONCAT('dbo.', required.TableName),
          required.ColumnName) IS NULL;

/* Task 39.1 journey activity IDs used by FAQ, enhancement and support creation. */
IF OBJECT_ID(N'dbo.OrganizationJourneyActivityType', N'U') IS NOT NULL
BEGIN
    INSERT INTO @Missing (Requirement)
    SELECT CONCAT(N'dbo.OrganizationJourneyActivityType ID ', required.ActivityTypeID)
    FROM (VALUES (99), (371), (372)) AS required(ActivityTypeID)
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.OrganizationJourneyActivityType AS actual
        WHERE actual.OrganizationJournalActivityTypeID = required.ActivityTypeID
    );
END;

/* One vote per organization per request requires this composite PK/unique key. */
IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS i
    INNER JOIN sys.index_columns AS ic1
        ON ic1.object_id = i.object_id
       AND ic1.index_id = i.index_id
       AND ic1.key_ordinal = 1
    INNER JOIN sys.columns AS c1
        ON c1.object_id = ic1.object_id
       AND c1.column_id = ic1.column_id
    INNER JOIN sys.index_columns AS ic2
        ON ic2.object_id = i.object_id
       AND ic2.index_id = i.index_id
       AND ic2.key_ordinal = 2
    INNER JOIN sys.columns AS c2
        ON c2.object_id = ic2.object_id
       AND c2.column_id = ic2.column_id
    WHERE i.object_id = OBJECT_ID(N'dbo.EnhancementRequestVote')
      AND i.is_unique = 1
      AND c1.name = N'OrganizationID'
      AND c2.name = N'EnhancementRequestID'
)
BEGIN
    INSERT INTO @Missing (Requirement)
    VALUES (N'Unique key on EnhancementRequestVote (OrganizationID, EnhancementRequestID)');
END;

/* Status rows supplied for Task 39. */
IF OBJECT_ID(N'dbo.EnhancementRequestStatus', N'U') IS NOT NULL
BEGIN
    INSERT INTO @Missing (Requirement)
    SELECT CONCAT(N'Enhancement status ', expected.StatusID, N' - ', expected.Description)
    FROM
    (
        VALUES
            (CONVERT(smallint, 10), 'Please Vote Before'),
            (CONVERT(smallint, 40), 'In Development Since'),
            (CONVERT(smallint, 97), 'Voted Down'),
            (CONVERT(smallint, 98), 'Declined by EMA'),
            (CONVERT(smallint, 99), 'Enhancement Completed')
    ) AS expected(StatusID, Description)
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.EnhancementRequestStatus AS actual
        WHERE actual.EnhancementRequestStatusID = expected.StatusID
          AND LTRIM(RTRIM(actual.Description)) = expected.Description
    );
END;

IF OBJECT_ID(N'dbo.SupportTicketStatus', N'U') IS NOT NULL
BEGIN
    INSERT INTO @Missing (Requirement)
    SELECT CONCAT(N'Support status ', expected.StatusID, N' - ', expected.Description)
    FROM
    (
        VALUES
            (CONVERT(smallint, 10), 'Reported'),
            (CONVERT(smallint, 20), 'Investigating'),
            (CONVERT(smallint, 30), 'Coding/Testing'),
            (CONVERT(smallint, 70), 'Resolved'),
            (CONVERT(smallint, 99), 'Closed')
    ) AS expected(StatusID, Description)
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.SupportTicketStatus AS actual
        WHERE actual.SupportTicketStatusID = expected.StatusID
          AND LTRIM(RTRIM(actual.Description)) = expected.Description
    );
END;

/* Insider menu rows already supplied by the database for Task 39. */
IF OBJECT_ID(N'dbo.MenuItem', N'U') IS NOT NULL
BEGIN
    INSERT INTO @Missing (Requirement)
    SELECT CONCAT(N'MenuItem ', expected.MenuItemID, N' ', expected.Url)
    FROM
    (
        VALUES
            (97920, '/ww-faqs'),
            (97930, '/ww-enhancements'),
            (97940, '/ww-tickets')
    ) AS expected(MenuItemID, Url)
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.MenuItem AS item
        WHERE item.MenuItemID = expected.MenuItemID
          AND item.URL = expected.Url
          AND item.IsActive = 'Y'
    );
END;

IF EXISTS (SELECT 1 FROM @Missing)
BEGIN
    SELECT Requirement AS MissingTask39Requirement
    FROM @Missing
    ORDER BY Requirement;

    THROW 51039, 'Task 39 database contract verification failed.', 1;
END;

SELECT
    'PASS' AS Task39DatabaseContract,
    'Required Task 39 tables, columns, statuses, vote key and Insider menu rows are present.' AS Detail;
