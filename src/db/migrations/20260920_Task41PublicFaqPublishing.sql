/*
    Task 41 - Copy FAQs to Public Website

    Adds the explicit CopyToPublic flag and allows Insider-created global FAQs
    to exist without a MenuItem reference.

    IMPORTANT: This script mutates database schema/data. Per EMA governance,
    review with Roger Osness before executing it against any database.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.FrequentlyAskedQuestions', N'U') IS NULL
        THROW 51100, 'Task 41 requires dbo.FrequentlyAskedQuestions.', 1;

    IF COL_LENGTH(N'dbo.FrequentlyAskedQuestions', N'MenuItemID') IS NULL
        THROW 51101, 'Task 41 requires FrequentlyAskedQuestions.MenuItemID.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM sys.columns AS c
        WHERE c.object_id = OBJECT_ID(N'dbo.FrequentlyAskedQuestions')
          AND c.name = N'MenuItemID'
          AND TYPE_NAME(c.user_type_id) <> N'int'
    )
        THROW 51102, 'FrequentlyAskedQuestions.MenuItemID is not INT; refusing to alter an unexpected schema.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.MenuItem
        WHERE LOWER(LTRIM(RTRIM(URL))) = '/ww-faqs'
          AND IsActive = 'Y'
    )
        THROW 51103, 'Task 41 requires an active /ww-faqs MenuItem for Journey logging.', 1;

    IF
    (
        SELECT COUNT(*)
        FROM dbo.MenuItem
        WHERE LOWER(LTRIM(RTRIM(URL))) = '/ww-faqs'
          AND IsActive = 'Y'
    ) <> 1
        THROW 51104, 'Task 41 expected exactly one active /ww-faqs MenuItem; refusing an ambiguous configuration.', 1;

    IF COL_LENGTH(N'dbo.FrequentlyAskedQuestions', N'CopyToPublic') IS NULL
    BEGIN
        ALTER TABLE dbo.FrequentlyAskedQuestions
        ADD CopyToPublic CHAR(1) NOT NULL
            CONSTRAINT DF_FrequentlyAskedQuestions_CopyToPublic DEFAULT ('N')
            WITH VALUES;
    END
    ELSE
    BEGIN
        IF EXISTS
        (
            SELECT 1
            FROM sys.columns AS c
            WHERE c.object_id = OBJECT_ID(N'dbo.FrequentlyAskedQuestions')
              AND c.name = N'CopyToPublic'
              AND
              (
                  TYPE_NAME(c.user_type_id) <> N'char'
                  OR c.max_length <> 1
              )
        )
            THROW 51105, 'FrequentlyAskedQuestions.CopyToPublic exists with an unexpected type; refusing to repurpose it.', 1;

        IF EXISTS
        (
            SELECT 1
            FROM dbo.FrequentlyAskedQuestions
            WHERE CopyToPublic IS NOT NULL
              AND UPPER(LTRIM(RTRIM(CopyToPublic))) NOT IN ('Y', 'N')
        )
            THROW 51106, 'FrequentlyAskedQuestions.CopyToPublic contains values other than Y/N.', 1;

        UPDATE dbo.FrequentlyAskedQuestions
        SET CopyToPublic = COALESCE(UPPER(LTRIM(RTRIM(CopyToPublic))), 'N')
        WHERE CopyToPublic IS NULL
           OR CopyToPublic <> COALESCE(UPPER(LTRIM(RTRIM(CopyToPublic))), 'N');

        IF EXISTS
        (
            SELECT 1
            FROM sys.columns AS c
            WHERE c.object_id = OBJECT_ID(N'dbo.FrequentlyAskedQuestions')
              AND c.name = N'CopyToPublic'
              AND c.is_nullable = 1
        )
        BEGIN
            ALTER TABLE dbo.FrequentlyAskedQuestions
            ALTER COLUMN CopyToPublic CHAR(1) NOT NULL;
        END;

        IF NOT EXISTS
        (
            SELECT 1
            FROM sys.default_constraints AS dc
            INNER JOIN sys.columns AS c
                ON c.object_id = dc.parent_object_id
               AND c.column_id = dc.parent_column_id
            WHERE dc.parent_object_id = OBJECT_ID(N'dbo.FrequentlyAskedQuestions')
              AND c.name = N'CopyToPublic'
        )
        BEGIN
            ALTER TABLE dbo.FrequentlyAskedQuestions
            ADD CONSTRAINT DF_FrequentlyAskedQuestions_CopyToPublic
                DEFAULT ('N') FOR CopyToPublic;
        END;
    END;

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
        THROW 51109, 'FrequentlyAskedQuestions.CopyToPublic must default to N.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.FrequentlyAskedQuestions')
          AND name = N'CK_FrequentlyAskedQuestions_CopyToPublic'
    )
    BEGIN
        ALTER TABLE dbo.FrequentlyAskedQuestions WITH CHECK
        ADD CONSTRAINT CK_FrequentlyAskedQuestions_CopyToPublic
            CHECK (CopyToPublic IN ('Y', 'N'));

        ALTER TABLE dbo.FrequentlyAskedQuestions
            CHECK CONSTRAINT CK_FrequentlyAskedQuestions_CopyToPublic;
    END;

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
        THROW 51110, 'Task 41 requires a trusted Y/N CopyToPublic check constraint.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM sys.columns AS c
        WHERE c.object_id = OBJECT_ID(N'dbo.FrequentlyAskedQuestions')
          AND c.name = N'MenuItemID'
          AND c.is_nullable = 0
    )
    BEGIN
        ALTER TABLE dbo.FrequentlyAskedQuestions
        ALTER COLUMN MenuItemID INT NULL;
    END;

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
        THROW 51107, 'Task 41 CopyToPublic contract was not established.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.columns AS c
        WHERE c.object_id = OBJECT_ID(N'dbo.FrequentlyAskedQuestions')
          AND c.name = N'MenuItemID'
          AND c.is_nullable = 1
    )
        THROW 51108, 'Task 41 requires FrequentlyAskedQuestions.MenuItemID to allow NULL.', 1;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
