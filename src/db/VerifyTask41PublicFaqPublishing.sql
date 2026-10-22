/*
    Task 41 - read-only database verifier.
    Performs no INSERT/UPDATE/DELETE/ALTER/CREATE/DROP operations.
*/
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.FrequentlyAskedQuestions', N'U') IS NULL
    THROW 51200, 'Missing dbo.FrequentlyAskedQuestions.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.columns AS c
    WHERE c.object_id = OBJECT_ID(N'dbo.FrequentlyAskedQuestions')
      AND c.name = N'CopyToPublic'
      AND TYPE_NAME(c.user_type_id) = N'char'
      AND c.max_length = 1
      AND c.is_nullable = 0
)
    THROW 51201, 'CopyToPublic must be CHAR(1) NOT NULL.', 1;

IF EXISTS
(
    SELECT 1
    FROM dbo.FrequentlyAskedQuestions
    WHERE CopyToPublic NOT IN ('Y', 'N')
)
    THROW 51202, 'CopyToPublic contains a value other than Y/N.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.default_constraints AS dc
    INNER JOIN sys.columns AS c
        ON c.object_id = dc.parent_object_id
       AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.FrequentlyAskedQuestions')
      AND c.name = N'CopyToPublic'
      AND UPPER(dc.definition) LIKE N'%''N''%'
)
    THROW 51203, 'CopyToPublic must have a default of N.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.check_constraints AS cc
    WHERE cc.parent_object_id = OBJECT_ID(N'dbo.FrequentlyAskedQuestions')
      AND cc.name = N'CK_FrequentlyAskedQuestions_CopyToPublic'
      AND cc.is_disabled = 0
      AND cc.is_not_trusted = 0
      AND UPPER(cc.definition) LIKE N'%COPYTOPUBLIC%'
      AND UPPER(cc.definition) LIKE N'%''Y''%'
      AND UPPER(cc.definition) LIKE N'%''N''%'
)
    THROW 51206, 'CopyToPublic is missing its trusted Y/N check constraint.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.columns AS c
    WHERE c.object_id = OBJECT_ID(N'dbo.FrequentlyAskedQuestions')
      AND c.name = N'MenuItemID'
      AND TYPE_NAME(c.user_type_id) = N'int'
      AND c.is_nullable = 1
)
    THROW 51204, 'FrequentlyAskedQuestions.MenuItemID must be nullable INT.', 1;

IF
(
    SELECT COUNT(*)
    FROM dbo.MenuItem
    WHERE LOWER(LTRIM(RTRIM(URL))) = '/ww-faqs'
      AND IsActive = 'Y'
) <> 1
    THROW 51205, 'Expected exactly one active /ww-faqs MenuItem.', 1;

SELECT
    COUNT(*) AS TotalFaqs,
    SUM(CASE WHEN CopyToPublic = 'Y' THEN 1 ELSE 0 END) AS CopyToPublicFaqs,
    SUM(CASE WHEN MenuItemID IS NULL THEN 1 ELSE 0 END) AS GlobalFaqs
FROM dbo.FrequentlyAskedQuestions;
