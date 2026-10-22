USE [emasqldatabase-dev];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
    Extends Task 9 menu-usage tracking for the security-patch ApiSession model.

    Existing LoginInstanceID and AuthenticationSessionID values are preserved.
    New authenticated requests use ApiSessionID.
*/

IF OBJECT_ID(N'dbo.MenuItemUsage', N'U') IS NULL
BEGIN
    THROW 51100, 'dbo.MenuItemUsage does not exist.', 1;
END;
GO

IF OBJECT_ID(N'dbo.ApiSession', N'U') IS NULL
BEGIN
    THROW 51101, 'dbo.ApiSession does not exist. Install the production security foundation first.', 1;
END;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF COL_LENGTH(
           N'dbo.MenuItemUsage',
           N'ApiSessionID') IS NULL
    BEGIN
        ALTER TABLE dbo.MenuItemUsage
            ADD ApiSessionID UNIQUEIDENTIFIER NULL;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id =
              OBJECT_ID(N'dbo.MenuItemUsage')
          AND name =
              N'CK_MenuItemUsage_SessionReference'
    )
    BEGIN
        ALTER TABLE dbo.MenuItemUsage
            DROP CONSTRAINT CK_MenuItemUsage_SessionReference;
    END;

    ALTER TABLE dbo.MenuItemUsage WITH CHECK
        ADD CONSTRAINT CK_MenuItemUsage_SessionReference
            CHECK
            (
                LoginInstanceID IS NOT NULL
                OR AuthenticationSessionID IS NOT NULL
                OR ApiSessionID IS NOT NULL
            );

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.foreign_key_columns AS foreignKeyColumn
        INNER JOIN sys.columns AS parentColumn
            ON parentColumn.object_id =
               foreignKeyColumn.parent_object_id
           AND parentColumn.column_id =
               foreignKeyColumn.parent_column_id
        INNER JOIN sys.columns AS referencedColumn
            ON referencedColumn.object_id =
               foreignKeyColumn.referenced_object_id
           AND referencedColumn.column_id =
               foreignKeyColumn.referenced_column_id
        WHERE foreignKeyColumn.parent_object_id =
              OBJECT_ID(N'dbo.MenuItemUsage')
          AND foreignKeyColumn.referenced_object_id =
              OBJECT_ID(N'dbo.ApiSession')
          AND parentColumn.name =
              N'ApiSessionID'
          AND referencedColumn.name =
              N'ApiSessionID'
    )
    BEGIN
        ALTER TABLE dbo.MenuItemUsage WITH CHECK
            ADD CONSTRAINT FK_MenuItemUsage_ApiSession
                FOREIGN KEY (ApiSessionID)
                REFERENCES dbo.ApiSession
                    (ApiSessionID);
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id =
              OBJECT_ID(N'dbo.MenuItemUsage')
          AND name =
              N'IX_MenuItemUsage_ApiSession_DateTimeLogged'
    )
    BEGIN
        CREATE INDEX IX_MenuItemUsage_ApiSession_DateTimeLogged
            ON dbo.MenuItemUsage
            (
                ApiSessionID,
                DateTimeLogged DESC
            )
            INCLUDE
            (
                MenuItemID,
                UserID
            )
            WHERE ApiSessionID IS NOT NULL;
    END;

    COMMIT TRANSACTION;

    PRINT 'MenuItemUsage ApiSession migration completed successfully.';
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
    BEGIN
        ROLLBACK TRANSACTION;
    END;

    THROW;
END CATCH;
GO

SELECT
    columnDefinition.column_id,
    columnDefinition.name AS ColumnName,
    typeDefinition.name AS DataType,
    columnDefinition.is_nullable AS IsNullable
FROM sys.columns AS columnDefinition
INNER JOIN sys.types AS typeDefinition
    ON typeDefinition.user_type_id =
       columnDefinition.user_type_id
WHERE columnDefinition.object_id =
      OBJECT_ID(N'dbo.MenuItemUsage')
  AND columnDefinition.name IN
      (
          N'LoginInstanceID',
          N'AuthenticationSessionID',
          N'ApiSessionID'
      )
ORDER BY columnDefinition.column_id;
GO
