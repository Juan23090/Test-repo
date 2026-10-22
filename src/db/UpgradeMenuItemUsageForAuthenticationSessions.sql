USE [emasqldatabase-dev];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
    MenuItemUsage migration v8

    Final design:
      MenuItemUsageID          BIGINT IDENTITY primary key
      LoginInstanceID          BIGINT NULL legacy login reference
      AuthenticationSessionID UNIQUEIDENTIFIER NULL new session reference
      MenuItemID               INT NOT NULL
      UserID                   INT NULL
      DateTimeLogged           DATETIME2(7) NOT NULL

    This version avoids renaming a constraint with sys.sp_rename.
*/

IF OBJECT_ID(N'dbo.MenuItemUsage', N'U') IS NULL
BEGIN
    THROW 51000, 'dbo.MenuItemUsage does not exist.', 1;
END;
GO

IF OBJECT_ID(N'dbo.LoginInstance', N'U') IS NULL
BEGIN
    THROW 51001, 'dbo.LoginInstance does not exist.', 1;
END;
GO

IF OBJECT_ID(N'dbo.AuthenticationSession', N'U') IS NULL
BEGIN
    THROW 51002, 'dbo.AuthenticationSession does not exist.', 1;
END;
GO

IF OBJECT_ID(N'dbo.MenuItem', N'U') IS NULL
BEGIN
    THROW 51003, 'dbo.MenuItem does not exist.', 1;
END;
GO

/*
    Stop rather than silently dropping a table referenced by another table.
*/
IF EXISTS
(
    SELECT 1
    FROM sys.foreign_keys
    WHERE referenced_object_id = OBJECT_ID(N'dbo.MenuItemUsage')
      AND parent_object_id <> OBJECT_ID(N'dbo.MenuItemUsage')
)
BEGIN
    THROW 51004,
        'Another table references dbo.MenuItemUsage. Review that foreign key before rebuilding the table.',
        1;
END;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @FinalStructureInstalled BIT =
        CASE
            WHEN COL_LENGTH(
                     N'dbo.MenuItemUsage',
                     N'MenuItemUsageID') IS NOT NULL
             AND COLUMNPROPERTY(
                     OBJECT_ID(N'dbo.MenuItemUsage'),
                     N'MenuItemUsageID',
                     'IsIdentity') = 1
             AND COLUMNPROPERTY(
                     OBJECT_ID(N'dbo.MenuItemUsage'),
                     N'LoginInstanceID',
                     'IsIdentity') = 0
                THEN 1
            ELSE 0
        END;

    IF @FinalStructureInstalled = 0
    BEGIN
        IF OBJECT_ID(
               N'dbo.MenuItemUsage_MigrationV8',
               N'U') IS NOT NULL
        BEGIN
            DROP TABLE dbo.MenuItemUsage_MigrationV8;
        END;

        /*
            Do not create the permanent primary key or default constraint
            until after the temporary table has been renamed.
        */
        CREATE TABLE dbo.MenuItemUsage_MigrationV8
        (
            MenuItemUsageID BIGINT IDENTITY(1,1) NOT NULL,
            LoginInstanceID BIGINT NULL,
            AuthenticationSessionID UNIQUEIDENTIFIER NULL,
            MenuItemID INT NOT NULL,
            UserID INT NULL,
            DateTimeLogged DATETIME2(7) NOT NULL
        );

        /*
            Preserve every existing usage record.
        */
        INSERT INTO dbo.MenuItemUsage_MigrationV8
        (
            LoginInstanceID,
            AuthenticationSessionID,
            MenuItemID,
            UserID,
            DateTimeLogged
        )
        SELECT
            LoginInstanceID,
            AuthenticationSessionID,
            MenuItemID,
            UserID,
            DateTimeLogged
        FROM dbo.MenuItemUsage;

        DROP TABLE dbo.MenuItemUsage;

        /*
            Renaming the table is supported. No constraint rename is needed.
        */
        EXEC sys.sp_rename
            N'dbo.MenuItemUsage_MigrationV8',
            N'MenuItemUsage';

        PRINT 'MenuItemUsage table structure was rebuilt.';
    END
    ELSE
    BEGIN
        PRINT 'The final MenuItemUsage table structure is already installed.';
    END;

    /*
        Validate the final column structure before adding constraints.
    */
    IF COL_LENGTH(
           N'dbo.MenuItemUsage',
           N'MenuItemUsageID') IS NULL
       OR COLUMNPROPERTY(
              OBJECT_ID(N'dbo.MenuItemUsage'),
              N'MenuItemUsageID',
              'IsIdentity') <> 1
       OR COLUMNPROPERTY(
              OBJECT_ID(N'dbo.MenuItemUsage'),
              N'LoginInstanceID',
              'IsIdentity') <> 0
    BEGIN
        THROW 51005,
            'The final MenuItemUsage identity structure was not created correctly.',
            1;
    END;

    /*
        Primary key
    */
    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.key_constraints
        WHERE parent_object_id =
              OBJECT_ID(N'dbo.MenuItemUsage')
          AND [type] = 'PK'
    )
    BEGIN
        ALTER TABLE dbo.MenuItemUsage
            ADD CONSTRAINT PK_MenuItemUsage
                PRIMARY KEY CLUSTERED
                (MenuItemUsageID);
    END
    ELSE IF NOT EXISTS
    (
        SELECT 1
        FROM sys.key_constraints AS keyConstraint
        INNER JOIN sys.index_columns AS indexColumn
            ON indexColumn.object_id =
               keyConstraint.parent_object_id
           AND indexColumn.index_id =
               keyConstraint.unique_index_id
        INNER JOIN sys.columns AS columnDefinition
            ON columnDefinition.object_id =
               indexColumn.object_id
           AND columnDefinition.column_id =
               indexColumn.column_id
        WHERE keyConstraint.parent_object_id =
              OBJECT_ID(N'dbo.MenuItemUsage')
          AND keyConstraint.[type] = 'PK'
          AND columnDefinition.name =
              N'MenuItemUsageID'
    )
    BEGIN
        THROW 51006,
            'dbo.MenuItemUsage has a primary key that does not use MenuItemUsageID.',
            1;
    END;

    /*
        DateTimeLogged default
    */
    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.default_constraints AS defaultConstraint
        INNER JOIN sys.columns AS columnDefinition
            ON columnDefinition.object_id =
               defaultConstraint.parent_object_id
           AND columnDefinition.column_id =
               defaultConstraint.parent_column_id
        WHERE defaultConstraint.parent_object_id =
              OBJECT_ID(N'dbo.MenuItemUsage')
          AND columnDefinition.name =
              N'DateTimeLogged'
    )
    BEGIN
        ALTER TABLE dbo.MenuItemUsage
            ADD CONSTRAINT DF_MenuItemUsage_DateTimeLogged
                DEFAULT (GETDATE()) FOR DateTimeLogged;
    END;

    /*
        Legacy LoginInstance foreign key
    */
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
              OBJECT_ID(N'dbo.LoginInstance')
          AND parentColumn.name =
              N'LoginInstanceID'
          AND referencedColumn.name =
              N'LoginInstanceID'
    )
    BEGIN
        ALTER TABLE dbo.MenuItemUsage WITH CHECK
            ADD CONSTRAINT FK_MenuItemUsage_LoginInstance
                FOREIGN KEY (LoginInstanceID)
                REFERENCES dbo.LoginInstance
                    (LoginInstanceID);
    END;

    /*
        New AuthenticationSession foreign key
    */
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
              OBJECT_ID(N'dbo.AuthenticationSession')
          AND parentColumn.name =
              N'AuthenticationSessionID'
          AND referencedColumn.name =
              N'AuthenticationSessionID'
    )
    BEGIN
        ALTER TABLE dbo.MenuItemUsage WITH CHECK
            ADD CONSTRAINT FK_MenuItemUsage_AuthenticationSession
                FOREIGN KEY (AuthenticationSessionID)
                REFERENCES dbo.AuthenticationSession
                    (AuthenticationSessionID);
    END;

    /*
        MenuItem foreign key
    */
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
              OBJECT_ID(N'dbo.MenuItem')
          AND parentColumn.name =
              N'MenuItemID'
          AND referencedColumn.name =
              N'MenuItemID'
    )
    BEGIN
        ALTER TABLE dbo.MenuItemUsage WITH CHECK
            ADD CONSTRAINT FK_MenuItemUsage_MenuItem
                FOREIGN KEY (MenuItemID)
                REFERENCES dbo.MenuItem
                    (MenuItemID);
    END;

    /*
        Every usage record must refer to either the legacy login model
        or the new server-managed authentication session.
    */
    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id =
              OBJECT_ID(N'dbo.MenuItemUsage')
          AND name =
              N'CK_MenuItemUsage_SessionReference'
    )
    BEGIN
        ALTER TABLE dbo.MenuItemUsage WITH CHECK
            ADD CONSTRAINT CK_MenuItemUsage_SessionReference
                CHECK
                (
                    LoginInstanceID IS NOT NULL
                    OR AuthenticationSessionID IS NOT NULL
                );
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id =
              OBJECT_ID(N'dbo.MenuItemUsage')
          AND name =
              N'IX_MenuItemUsage_LoginInstance_DateTimeLogged'
    )
    BEGIN
        CREATE INDEX IX_MenuItemUsage_LoginInstance_DateTimeLogged
            ON dbo.MenuItemUsage
            (
                LoginInstanceID,
                DateTimeLogged DESC
            )
            INCLUDE
            (
                MenuItemID,
                UserID
            )
            WHERE LoginInstanceID IS NOT NULL;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id =
              OBJECT_ID(N'dbo.MenuItemUsage')
          AND name =
              N'IX_MenuItemUsage_AuthenticationSession_DateTimeLogged'
    )
    BEGIN
        CREATE INDEX IX_MenuItemUsage_AuthenticationSession_DateTimeLogged
            ON dbo.MenuItemUsage
            (
                AuthenticationSessionID,
                DateTimeLogged DESC
            )
            INCLUDE
            (
                MenuItemID,
                UserID
            )
            WHERE AuthenticationSessionID IS NOT NULL;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id =
              OBJECT_ID(N'dbo.MenuItemUsage')
          AND name =
              N'IX_MenuItemUsage_MenuItem_DateTimeLogged'
    )
    BEGIN
        CREATE INDEX IX_MenuItemUsage_MenuItem_DateTimeLogged
            ON dbo.MenuItemUsage
            (
                MenuItemID,
                DateTimeLogged DESC
            );
    END;

    COMMIT TRANSACTION;

    PRINT 'MenuItemUsage migration v8 completed successfully.';
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
    BEGIN
        ROLLBACK TRANSACTION;
    END;

    THROW;
END CATCH;
GO

/*
    Schema verification
*/
SELECT
    columnDefinition.column_id,
    columnDefinition.name AS ColumnName,
    typeDefinition.name AS DataType,
    columnDefinition.is_nullable AS IsNullable,
    columnDefinition.is_identity AS IsIdentity
FROM sys.columns AS columnDefinition
INNER JOIN sys.types AS typeDefinition
    ON typeDefinition.user_type_id =
       columnDefinition.user_type_id
WHERE columnDefinition.object_id =
      OBJECT_ID(N'dbo.MenuItemUsage')
ORDER BY columnDefinition.column_id;
GO

/*
    Constraint verification
*/
SELECT
    foreignKey.name AS ForeignKeyName,
    parentColumn.name AS ParentColumn,
    OBJECT_NAME(
        foreignKey.referenced_object_id) AS ReferencedTable,
    referencedColumn.name AS ReferencedColumn
FROM sys.foreign_keys AS foreignKey
INNER JOIN sys.foreign_key_columns AS foreignKeyColumn
    ON foreignKeyColumn.constraint_object_id =
       foreignKey.object_id
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
WHERE foreignKey.parent_object_id =
      OBJECT_ID(N'dbo.MenuItemUsage')
ORDER BY foreignKey.name;
GO

/*
    Avoid a compile-time error if an unexpected earlier migration failure
    left the table in its legacy shape.
*/
IF COL_LENGTH(
       N'dbo.MenuItemUsage',
       N'MenuItemUsageID') IS NOT NULL
BEGIN
    EXEC sys.sp_executesql N'
        SELECT TOP 50
            usage.MenuItemUsageID,
            usage.LoginInstanceID,
            usage.AuthenticationSessionID,
            usage.MenuItemID,
            menuItem.MenuName,
            menuItem.ScreenTitle,
            usage.UserID,
            usage.DateTimeLogged,
            authenticationSession.DonorID,
            authenticationSession.EMAOrganizationID,
            authenticationSession.IsDonorSession
        FROM dbo.MenuItemUsage AS usage
        INNER JOIN dbo.MenuItem AS menuItem
            ON menuItem.MenuItemID =
               usage.MenuItemID
        LEFT JOIN dbo.AuthenticationSession AS authenticationSession
            ON authenticationSession.AuthenticationSessionID =
               usage.AuthenticationSessionID
        ORDER BY usage.DateTimeLogged DESC;';
END;
GO
