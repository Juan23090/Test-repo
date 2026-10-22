/*
    EMA USER SANDBOX BACKEND
    ------------------------
    Run this script once against the EMA database after reviewing the settings
    used by the API:

        SandboxTemplateOrganizationID
        SandboxTemplateUserID (optional)
        SandboxEmailDomain
        SandboxCreatedByUserID
        SandboxMaxTemplateDonations (optional, default 100)
        SandboxActiveDays (optional, default 180)

    The script is idempotent. It creates:
      - supporting indexes
      - dbo.sp_SandboxCloneIdentityTable
      - dbo.sp_SandboxCloneDirectTable
      - dbo.sp_SandboxCreate
      - dbo.sp_SandboxDeleteOne
      - dbo.sp_SandboxDeleteExpired
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/* -------------------------------------------------------------------------
   Required schema checks
   ------------------------------------------------------------------------- */

IF COL_LENGTH(
    'dbo.Organization',
    'SandboxEtransferEmail'
) IS NULL
BEGIN
    ALTER TABLE dbo.Organization
        ADD SandboxEtransferEmail NVARCHAR(512) NULL;
END;
GO

IF COL_LENGTH(
    'dbo.[User]',
    'SandboxIPAddress'
) IS NULL
BEGIN
    ALTER TABLE dbo.[User]
        ADD SandboxIPAddress VARCHAR(50) NULL;
END;
GO

IF COL_LENGTH(
    'dbo.[User]',
    'SandboxOrganizationID'
) IS NULL
BEGIN
    ALTER TABLE dbo.[User]
        ADD SandboxOrganizationID INT NULL;
END;
GO

IF OBJECT_ID(
    'dbo.OrganizationStatus',
    'U'
) IS NOT NULL
   AND NOT EXISTS
   (
       SELECT 1
       FROM dbo.OrganizationStatus
       WHERE OrganizationStatusID = 0
   )
BEGIN
    IF COLUMNPROPERTY(
        OBJECT_ID('dbo.OrganizationStatus'),
        'OrganizationStatusID',
        'IsIdentity'
    ) = 1
        SET IDENTITY_INSERT dbo.OrganizationStatus ON;

    INSERT INTO dbo.OrganizationStatus
    (
        OrganizationStatusID,
        Sequence,
        Description
    )
    VALUES
    (
        0,
        0,
        'Sandbox'
    );

    IF COLUMNPROPERTY(
        OBJECT_ID('dbo.OrganizationStatus'),
        'OrganizationStatusID',
        'IsIdentity'
    ) = 1
        SET IDENTITY_INSERT dbo.OrganizationStatus OFF;
END;
GO

IF EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id =
          OBJECT_ID('dbo.[User]')
      AND name =
          'IX_User_SandboxIPAddress'
)
BEGIN
    DROP INDEX IX_User_SandboxIPAddress
        ON dbo.[User];
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id =
          OBJECT_ID('dbo.[User]')
      AND name =
          'UX_User_SandboxIPAddress'
)
BEGIN
    CREATE UNIQUE INDEX UX_User_SandboxIPAddress
        ON dbo.[User]
        (
            SandboxIPAddress
        )
        INCLUDE
        (
            UserID,
            SandboxOrganizationID,
            PhoneNumber,
            IsActive
        )
        WHERE SandboxIPAddress IS NOT NULL;
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id =
          OBJECT_ID('dbo.[User]')
      AND name =
          'IX_User_SandboxOrganizationID'
)
BEGIN
    CREATE INDEX IX_User_SandboxOrganizationID
        ON dbo.[User]
        (
            SandboxOrganizationID
        )
        INCLUDE
        (
            UserID,
            SandboxIPAddress,
            PhoneNumber,
            IsActive
        )
        WHERE SandboxOrganizationID IS NOT NULL;
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id =
          OBJECT_ID('dbo.Organization')
      AND name =
          'UX_Organization_SandboxEtransferEmail'
)
BEGIN
    CREATE UNIQUE INDEX UX_Organization_SandboxEtransferEmail
        ON dbo.Organization
        (
            SandboxEtransferEmail
        )
        WHERE SandboxEtransferEmail IS NOT NULL;
END;
GO

/* -------------------------------------------------------------------------
   Internal helper: clone an identity-based organization-owned table.

   The procedure deliberately uses sys.columns so it follows the real database
   column list rather than hard-coding every audit/configuration column.

   It also remaps the known daughter-table foreign keys through
   #SandboxCloneMap.
   ------------------------------------------------------------------------- */

CREATE OR ALTER PROCEDURE dbo.sp_SandboxCloneIdentityTable
    @EntityName              SYSNAME,
    @TableName               SYSNAME,
    @IdentityColumn          SYSNAME,
    @OrganizationColumn      SYSNAME,
    @SourceOrganizationID    INT,
    @TargetOrganizationID    INT,
    @CreatedByUserID         INT,
    @PhoneNumber             NVARCHAR(20),
    @SandboxEmail            NVARCHAR(512),
    @DonationDateShiftDays   INT = 0,
    @MaxRows                 INT = 2147483647,
    @OrderByClause           NVARCHAR(1000) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF OBJECT_ID(
        N'dbo.' + QUOTENAME(@TableName),
        N'U'
    ) IS NULL
        RETURN;

    IF COL_LENGTH(
        N'dbo.' + @TableName,
        @IdentityColumn
    ) IS NULL
        RETURN;

    IF COL_LENGTH(
        N'dbo.' + @TableName,
        @OrganizationColumn
    ) IS NULL
        RETURN;

    IF OBJECT_ID(
        'tempdb..#SandboxCloneMap'
    ) IS NULL
        THROW 51030,
            '#SandboxCloneMap was not created by dbo.sp_SandboxCreate.',
            1;

    IF OBJECT_ID(
        'tempdb..#SandboxCloneQueue'
    ) IS NULL
        THROW 51031,
            '#SandboxCloneQueue was not created by dbo.sp_SandboxCreate.',
            1;

    TRUNCATE TABLE #SandboxCloneQueue;

    DECLARE
        @QualifiedTableName NVARCHAR(520) =
            QUOTENAME(N'dbo') +
            N'.' +
            QUOTENAME(@TableName),

        @QueueSql NVARCHAR(MAX),
        @InsertColumns NVARCHAR(MAX),
        @SelectColumns NVARCHAR(MAX),
        @InsertSql NVARCHAR(MAX),
        @AllocateKeySql NVARCHAR(MAX),
        @TableObjectID INT,
        @KeyIsIdentity BIT = 0,
        @OldID BIGINT,
        @NewID BIGINT;

    SET @TableObjectID =
        OBJECT_ID(
            @QualifiedTableName
        );

    SET @KeyIsIdentity =
        CASE
            WHEN COLUMNPROPERTY(
                @TableObjectID,
                @IdentityColumn,
                'IsIdentity'
            ) = 1
                THEN 1
            ELSE 0
        END;

    SET @MaxRows =
        CASE
            WHEN @MaxRows IS NULL
              OR @MaxRows <= 0
                THEN 2147483647
            ELSE @MaxRows
        END;

    SET @QueueSql =
        N'INSERT INTO #SandboxCloneQueue (OldID)
          SELECT TOP (@TakeRows)
              CONVERT(BIGINT, src.' +
              QUOTENAME(@IdentityColumn) +
              N')
          FROM ' +
              @QualifiedTableName +
              N' src
          WHERE src.' +
              QUOTENAME(@OrganizationColumn) +
              N' = @SourceOrganizationID' +

          CASE
              WHEN NULLIF(
                  LTRIM(RTRIM(@OrderByClause)),
                  N''
              ) IS NULL
                  THEN
                      N' ORDER BY src.' +
                      QUOTENAME(@IdentityColumn)
              ELSE
                      N' ORDER BY ' +
                      @OrderByClause
          END +

          N';';

    EXEC sys.sp_executesql
        @QueueSql,
        N'@TakeRows INT,
          @SourceOrganizationID INT',
        @TakeRows =
            @MaxRows,
        @SourceOrganizationID =
            @SourceOrganizationID;

    SELECT
        @InsertColumns =
            STRING_AGG(
                CONVERT(
                    NVARCHAR(MAX),
                    QUOTENAME(c.name)
                ),
                N', '
            ) WITHIN GROUP
            (
                ORDER BY c.column_id
            ),

        @SelectColumns =
            STRING_AGG(
                CONVERT(
                    NVARCHAR(MAX),

                    CASE
                        WHEN c.name =
                             @IdentityColumn
                         AND @KeyIsIdentity = 0
                            THEN
                                N'@NewID'

                        WHEN c.name =
                             @OrganizationColumn
                            THEN
                                N'@TargetOrganizationID'

                        WHEN c.name IN
                        (
                            N'DateTimeCreated',
                            N'DatetimeCreated',
                            N'DateTImeCreated',
                            N'DateCreated',
                            N'CreatedDate'
                        )
                            THEN
                                N'SYSUTCDATETIME()'

                        WHEN c.name IN
                        (
                            N'DateTimeUpdated',
                            N'DatetimeUpdated',
                            N'DateUpdated',
                            N'UpdatedDate'
                        )
                            THEN
                                N'SYSUTCDATETIME()'

                        WHEN c.name IN
                        (
                            N'CreatedByUserID',
                            N'CreatedbyUserID',
                            N'CreatedBuyUserID',
                            N'CreateddbyUserID',
                            N'UserIDWhoCreated',
                            N'UserIDWhoCrearted'
                        )
                            THEN
                                N'@CreatedByUserID'

                        WHEN c.name IN
                        (
                            N'UpdatedByUserID',
                            N'UpdatedbyUserID',
                            N'UserIDWhoUpdated'
                        )
                            THEN
                                N'@CreatedByUserID'

                        WHEN @EntityName = N'Campus'
                         AND c.name =
                             N'DefaultCampusFundID'
                            THEN
                                N'NULL'

                        WHEN @EntityName = N'Fund'
                         AND c.name =
                             N'EMACampusID'
                            THEN
                                N'(
                                    SELECT TOP (1)
                                        CONVERT(
                                            INT,
                                            map.NewID
                                        )
                                    FROM #SandboxCloneMap map
                                    WHERE map.EntityName =
                                          N''Campus''
                                      AND map.OldID =
                                          CONVERT(
                                              BIGINT,
                                              src.' +
                                              QUOTENAME(c.name) +
                                              N'
                                          )
                                )'

                        WHEN @EntityName = N'Donor'
                         AND c.name =
                             N'EMADefaultCampusID'
                            THEN
                                N'(
                                    SELECT TOP (1)
                                        CONVERT(
                                            INT,
                                            map.NewID
                                        )
                                    FROM #SandboxCloneMap map
                                    WHERE map.EntityName =
                                          N''Campus''
                                      AND map.OldID =
                                          CONVERT(
                                              BIGINT,
                                              src.' +
                                              QUOTENAME(c.name) +
                                              N'
                                          )
                                )'

                        WHEN @EntityName = N'Donor'
                         AND c.name =
                             N'EmailAddress'
                            THEN
                                N'CONCAT(
                                    N''sandbox-donor-'',
                                    CONVERT(
                                        NVARCHAR(30),
                                        src.' +
                                        QUOTENAME(
                                            @IdentityColumn
                                        ) +
                                        N'
                                    ),
                                    N''-'',
                                    @PhoneNumber,
                                    N''@example.invalid''
                                )'

                        WHEN @EntityName = N'Donor'
                         AND c.name =
                             N'PhoneNumber'
                            THEN
                                N'NULL'

                        WHEN @EntityName = N'Donor'
                         AND c.name =
                             N'GivingNumber'
                            THEN
                                N'LEFT(
                                    CONCAT(
                                        N''SBX-'',
                                        @PhoneNumber,
                                        N''-'',
                                        CONVERT(
                                            NVARCHAR(30),
                                            src.' +
                                            QUOTENAME(
                                                @IdentityColumn
                                            ) +
                                            N'
                                        )
                                    ),
                                    255
                                )'

                        WHEN @EntityName = N'Donor'
                         AND c.name =
                             N'DonorStatusID'
                            THEN
                                N'(
                                    SELECT TOP (1)
                                        CONVERT(
                                            SMALLINT,
                                            map.NewID
                                        )
                                    FROM #SandboxCloneMap map
                                    WHERE map.EntityName =
                                          N''DonorStatus''
                                      AND map.OldID =
                                          CONVERT(
                                              BIGINT,
                                              src.' +
                                              QUOTENAME(c.name) +
                                              N'
                                          )
                                )'

                        WHEN @EntityName = N'Donation'
                         AND c.name =
                             N'EMADonorID'
                            THEN
                                N'(
                                    SELECT TOP (1)
                                        CONVERT(
                                            INT,
                                            map.NewID
                                        )
                                    FROM #SandboxCloneMap map
                                    WHERE map.EntityName =
                                          N''Donor''
                                      AND map.OldID =
                                          CONVERT(
                                              BIGINT,
                                              src.' +
                                              QUOTENAME(c.name) +
                                              N'
                                          )
                                )'

                        WHEN @EntityName = N'Donation'
                         AND c.name =
                             N'EMAMethodOfGivingID'
                            THEN
                                N'(
                                    SELECT TOP (1)
                                        CONVERT(
                                            INT,
                                            map.NewID
                                        )
                                    FROM #SandboxCloneMap map
                                    WHERE map.EntityName =
                                          N''Method''
                                      AND map.OldID =
                                          CONVERT(
                                              BIGINT,
                                              src.' +
                                              QUOTENAME(c.name) +
                                              N'
                                          )
                                )'

                        WHEN @EntityName = N'Donation'
                         AND c.name =
                             N'EMAEventID'
                            THEN
                                N'(
                                    SELECT TOP (1)
                                        CONVERT(
                                            INT,
                                            map.NewID
                                        )
                                    FROM #SandboxCloneMap map
                                    WHERE map.EntityName =
                                          N''Event''
                                      AND map.OldID =
                                          CONVERT(
                                              BIGINT,
                                              src.' +
                                              QUOTENAME(c.name) +
                                              N'
                                          )
                                )'

                        WHEN @EntityName = N'Donation'
                         AND c.name =
                             N'EMADonationSourceID'
                            THEN
                                N'(
                                    SELECT TOP (1)
                                        CONVERT(
                                            INT,
                                            map.NewID
                                        )
                                    FROM #SandboxCloneMap map
                                    WHERE map.EntityName =
                                          N''DonationSource''
                                      AND map.OldID =
                                          CONVERT(
                                              BIGINT,
                                              src.' +
                                              QUOTENAME(c.name) +
                                              N'
                                          )
                                )'

                        WHEN @EntityName = N'Donation'
                         AND c.name =
                             N'EMATributeTypeID'
                            THEN
                                N'(
                                    SELECT TOP (1)
                                        CONVERT(
                                            INT,
                                            map.NewID
                                        )
                                    FROM #SandboxCloneMap map
                                    WHERE map.EntityName =
                                          N''TributeType''
                                      AND map.OldID =
                                          CONVERT(
                                              BIGINT,
                                              src.' +
                                              QUOTENAME(c.name) +
                                              N'
                                          )
                                )'

                        WHEN @EntityName = N'Donation'
                         AND c.name =
                             N'EMAReceiptID'
                            THEN
                                N'NULL'

                        WHEN @EntityName = N'Donation'
                         AND c.name IN
                         (
                            N'EmailBlobID',
                            N'GlobalMessageID',
                            N'PaymentKey',
                            N'PaymentNotificationID'
                         )
                            THEN
                                N'NULL'

                        WHEN @EntityName = N'Donation'
                         AND c.name =
                             N'RecipientEmailAddress'
                            THEN
                                N'@SandboxEmail'

                        WHEN @EntityName = N'Donation'
                         AND c.name =
                             N'ReferenceNumber'
                            THEN
                                N'LEFT(
                                    CONCAT(
                                        N''SBX-'',
                                        @PhoneNumber,
                                        N''-'',
                                        CONVERT(
                                            NVARCHAR(30),
                                            src.' +
                                            QUOTENAME(
                                                @IdentityColumn
                                            ) +
                                            N'
                                        )
                                    ),
                                    255
                                )'

                        WHEN @EntityName = N'Donation'
                         AND c.name =
                             N'DateTimeReceived'
                            THEN
                                N'CASE
                                    WHEN src.' +
                                         QUOTENAME(c.name) +
                                         N' IS NULL
                                        THEN
                                            SYSUTCDATETIME()
                                    ELSE
                                        DATEADD(
                                            DAY,
                                            @DonationDateShiftDays,
                                            src.' +
                                            QUOTENAME(c.name) +
                                            N'
                                        )
                                END'

                        WHEN @EntityName = N'Donation'
                         AND c.name =
                             N'DeadlineToMakeFundChanges'
                            THEN
                                N'CASE
                                    WHEN src.' +
                                         QUOTENAME(c.name) +
                                         N' IS NULL
                                        THEN
                                            NULL
                                    ELSE
                                        DATEADD(
                                            DAY,
                                            @DonationDateShiftDays,
                                            src.' +
                                            QUOTENAME(c.name) +
                                            N'
                                        )
                                END'

                        ELSE
                            N'src.' +
                            QUOTENAME(c.name)
                    END
                ),
                N', '
            ) WITHIN GROUP
            (
                ORDER BY c.column_id
            )

    FROM sys.columns c

    WHERE c.object_id =
          OBJECT_ID(
              @QualifiedTableName
          )
      AND c.is_identity = 0
      AND c.is_computed = 0
      AND c.system_type_id <> 189;

    IF NULLIF(
        @InsertColumns,
        N''
    ) IS NULL
       OR NULLIF(
           @SelectColumns,
           N''
       ) IS NULL
        RETURN;

    DECLARE clone_cursor
        CURSOR LOCAL FAST_FORWARD FOR

        SELECT OldID
        FROM #SandboxCloneQueue
        ORDER BY QueueID;

    OPEN clone_cursor;

    FETCH NEXT FROM clone_cursor
        INTO @OldID;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @NewID = NULL;

        IF @KeyIsIdentity = 0
        BEGIN
            SET @AllocateKeySql =
                N'SELECT @NewID =
                      ISNULL(
                          MAX(
                              CONVERT(
                                  BIGINT,
                                  ' +
                                  QUOTENAME(
                                      @IdentityColumn
                                  ) +
                                  N'
                              )
                          ),
                          0
                      ) + 1
                  FROM ' +
                  @QualifiedTableName +
                  N' WITH (
                      UPDLOCK,
                      HOLDLOCK
                  );';

            EXEC sys.sp_executesql
                @AllocateKeySql,
                N'@NewID BIGINT OUTPUT',
                @NewID =
                    @NewID OUTPUT;
        END;

        SET @InsertSql =
            N'INSERT INTO ' +
            @QualifiedTableName +
            N'
              (
                  ' +
                  @InsertColumns +
                  N'
              )
              SELECT
                  ' +
                  @SelectColumns +
                  N'
              FROM ' +
                  @QualifiedTableName +
                  N' src
              WHERE CONVERT(
                  BIGINT,
                  src.' +
                  QUOTENAME(
                      @IdentityColumn
                  ) +
                  N'
              ) = @OldID;

              IF @KeyIsIdentity = 1
                  SET @NewID =
                      CONVERT(
                          BIGINT,
                          SCOPE_IDENTITY()
                      );';

        EXEC sys.sp_executesql
            @InsertSql,

            N'@OldID BIGINT,
              @TargetOrganizationID INT,
              @CreatedByUserID INT,
              @PhoneNumber NVARCHAR(20),
              @SandboxEmail NVARCHAR(512),
              @DonationDateShiftDays INT,
              @KeyIsIdentity BIT,
              @NewID BIGINT OUTPUT',

            @OldID =
                @OldID,

            @TargetOrganizationID =
                @TargetOrganizationID,

            @CreatedByUserID =
                @CreatedByUserID,

            @PhoneNumber =
                @PhoneNumber,

            @SandboxEmail =
                @SandboxEmail,

            @DonationDateShiftDays =
                @DonationDateShiftDays,

            @KeyIsIdentity =
                @KeyIsIdentity,

            @NewID =
                @NewID OUTPUT;

        IF @NewID IS NULL
            THROW 51032,
                'A sandbox daughter-table insert did not return a key value.',
                1;

        INSERT INTO #SandboxCloneMap
        (
            EntityName,
            OldID,
            NewID
        )
        VALUES
        (
            @EntityName,
            @OldID,
            @NewID
        );

        FETCH NEXT FROM clone_cursor
            INTO @OldID;
    END;

    CLOSE clone_cursor;
    DEALLOCATE clone_cursor;
END;
GO

/* -------------------------------------------------------------------------
   Internal helper: clone a direct organization table when no ID map is needed.
   This is used only for optional configuration tables that are independent of
   the core Campus/Fund/Donor/Donation graph.
   ------------------------------------------------------------------------- */

CREATE OR ALTER PROCEDURE dbo.sp_SandboxCloneDirectTable
    @TableName               SYSNAME,
    @OrganizationColumn      SYSNAME,
    @SourceOrganizationID    INT,
    @TargetOrganizationID    INT,
    @CreatedByUserID         INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF OBJECT_ID(
        N'dbo.' + QUOTENAME(@TableName),
        N'U'
    ) IS NULL
        RETURN;

    IF COL_LENGTH(
        N'dbo.' + @TableName,
        @OrganizationColumn
    ) IS NULL
        RETURN;

    DECLARE
        @QualifiedTableName NVARCHAR(520) =
            QUOTENAME(N'dbo') +
            N'.' +
            QUOTENAME(@TableName),

        @InsertColumns NVARCHAR(MAX),
        @SelectColumns NVARCHAR(MAX),
        @Sql NVARCHAR(MAX);

    SELECT
        @InsertColumns =
            STRING_AGG(
                CONVERT(
                    NVARCHAR(MAX),
                    QUOTENAME(c.name)
                ),
                N', '
            ) WITHIN GROUP
            (
                ORDER BY c.column_id
            ),

        @SelectColumns =
            STRING_AGG(
                CONVERT(
                    NVARCHAR(MAX),

                    CASE
                        WHEN c.name =
                             @OrganizationColumn
                            THEN
                                N'@TargetOrganizationID'

                        WHEN c.name IN
                        (
                            N'DateTimeCreated',
                            N'DatetimeCreated',
                            N'DateTImeCreated',
                            N'DateCreated',
                            N'CreatedDate',
                            N'DateTimeUpdated',
                            N'DatetimeUpdated',
                            N'DateUpdated',
                            N'UpdatedDate'
                        )
                            THEN
                                N'SYSUTCDATETIME()'

                        WHEN c.name IN
                        (
                            N'CreatedByUserID',
                            N'CreatedbyUserID',
                            N'CreatedBuyUserID',
                            N'CreateddbyUserID',
                            N'UserIDWhoCreated',
                            N'UserIDWhoCrearted',
                            N'UpdatedByUserID',
                            N'UpdatedbyUserID',
                            N'UserIDWhoUpdated'
                        )
                            THEN
                                N'@CreatedByUserID'

                        ELSE
                            N'src.' +
                            QUOTENAME(c.name)
                    END
                ),
                N', '
            ) WITHIN GROUP
            (
                ORDER BY c.column_id
            )

    FROM sys.columns c

    WHERE c.object_id =
          OBJECT_ID(
              @QualifiedTableName
          )
      AND c.is_identity = 0
      AND c.is_computed = 0
      AND c.system_type_id <> 189;

    IF NULLIF(
        @InsertColumns,
        N''
    ) IS NULL
       OR NULLIF(
           @SelectColumns,
           N''
       ) IS NULL
        RETURN;

    SET @Sql =
        N'INSERT INTO ' +
        @QualifiedTableName +
        N'
          (
              ' +
              @InsertColumns +
              N'
          )
          SELECT
              ' +
              @SelectColumns +
              N'
          FROM ' +
              @QualifiedTableName +
              N' src
          WHERE src.' +
              QUOTENAME(
                  @OrganizationColumn
              ) +
              N' = @SourceOrganizationID;';

    EXEC sys.sp_executesql
        @Sql,

        N'@SourceOrganizationID INT,
          @TargetOrganizationID INT,
          @CreatedByUserID INT',

        @SourceOrganizationID =
            @SourceOrganizationID,

        @TargetOrganizationID =
            @TargetOrganizationID,

        @CreatedByUserID =
            @CreatedByUserID;
END;
GO

/* -------------------------------------------------------------------------
   Create or return one active sandbox for the requesting IP.
   ------------------------------------------------------------------------- */

CREATE OR ALTER PROCEDURE dbo.sp_SandboxCreate
    @TemplateOrganizationID INT,
    @TemplateUserID         INT = NULL,
    @PhoneNumber            NVARCHAR(20),
    @SandboxIPAddress       VARCHAR(50),
    @SandboxEmail           NVARCHAR(512),
    @CreatedByUserID        INT = 1,
    @MaxTemplateDonations   INT = 100,
    @ActiveDays             INT = 180
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @PhoneNumber =
        NULLIF(
            REPLACE(
                REPLACE(
                    REPLACE(
                        REPLACE(
                            REPLACE(
                                REPLACE(
                                    LTRIM(
                                        RTRIM(
                                            @PhoneNumber
                                        )
                                    ),
                                    '(',
                                    ''
                                ),
                                ')',
                                ''
                            ),
                            '-',
                            ''
                        ),
                        ' ',
                        ''
                    ),
                    '.',
                    ''
                ),
                '+',
                ''
            ),
            ''
        );

    SET @SandboxIPAddress =
        LEFT(
            NULLIF(
                LTRIM(
                    RTRIM(
                        @SandboxIPAddress
                    )
                ),
                ''
            ),
            50
        );

    SET @SandboxEmail =
        LOWER(
            LTRIM(
                RTRIM(
                    @SandboxEmail
                )
            )
        );

    IF @TemplateOrganizationID <= 0
        THROW 51001,
            'A valid template organization is required.',
            1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.Organization
        WHERE EMAOrganizationID =
              @TemplateOrganizationID
    )
        THROW 51002,
            'The configured sandbox template organization does not exist.',
            1;

    IF LEN(@PhoneNumber) <> 10
       OR @PhoneNumber LIKE '%[^0-9]%'
       OR LEFT(
           @PhoneNumber,
           1
       ) IN
       (
           '0',
           '1'
       )
        THROW 51003,
            'The sandbox phone number must contain 10 digits and cannot begin with 0 or 1.',
            1;

    IF @SandboxIPAddress IS NULL
        THROW 51005,
            'A client IP address is required to create a sandbox.',
            1;

    IF @SandboxEmail IS NULL
       OR @SandboxEmail NOT LIKE '%_@_%._%'
        THROW 51006,
            'A valid sandbox email address is required.',
            1;

    IF @MaxTemplateDonations IS NULL
       OR @MaxTemplateDonations <= 0
        SET @MaxTemplateDonations = 100;

    IF @ActiveDays IS NULL
       OR @ActiveDays <= 0
        SET @ActiveDays = 180;

    BEGIN TRANSACTION;

    BEGIN TRY
        DECLARE
            @ApplicationLockResult INT,

            @ApplicationLockName NVARCHAR(255) =
                CONCAT(
                    N'EMA-SANDBOX-IP-',
                    @SandboxIPAddress
                );

        EXEC @ApplicationLockResult =
            sys.sp_getapplock
                @Resource =
                    @ApplicationLockName,

                @LockMode =
                    'Exclusive',

                @LockOwner =
                    'Transaction',

                @LockTimeout =
                    15000;

        IF @ApplicationLockResult < 0
            THROW 51007,
                'The sandbox request could not obtain its creation lock.',
                1;

        IF EXISTS
        (
            SELECT 1
            FROM dbo.[User] u WITH
            (
                UPDLOCK,
                HOLDLOCK
            )

            INNER JOIN dbo.Organization o
                ON o.EMAOrganizationID =
                   u.SandboxOrganizationID

            OUTER APPLY
            (
                SELECT
                    MAX(d.DateTimeReceived) AS LastDonationDate
                FROM dbo.Donation d
                WHERE d.EMAOrganizationID =
                      o.EMAOrganizationID
            ) activity

            WHERE u.IsActive = 1
              AND u.SandboxIPAddress =
                  @SandboxIPAddress
              AND u.SandboxOrganizationID IS NOT NULL
              AND ISNULL(
                  o.OrganizationStatusID,
                  -1
              ) = 0
              AND COALESCE(
                    activity.LastDonationDate,
                    o.DateTimeCreated,
                    u.DateTimeCreated,
                    SYSUTCDATETIME()
                  ) >= DATEADD(
                        DAY,
                        -@ActiveDays,
                        SYSUTCDATETIME()
                  )
        )
        BEGIN
            COMMIT TRANSACTION;

            SELECT TOP (1)
                o.EMAOrganizationID AS SandboxOrganizationID,
                u.UserID AS SandboxUserID,
                o.OrganizationName,
                u.PhoneNumber,
                o.SandboxEtransferEmail AS SandboxEmailAddress,
                o.DateTimeCreated,
                activity.LastDonationDate

            FROM dbo.[User] u

            INNER JOIN dbo.Organization o
                ON o.EMAOrganizationID =
                   u.SandboxOrganizationID

            OUTER APPLY
            (
                SELECT
                    MAX(d.DateTimeReceived) AS LastDonationDate
                FROM dbo.Donation d
                WHERE d.EMAOrganizationID =
                      o.EMAOrganizationID
            ) activity

            WHERE u.IsActive = 1
              AND u.SandboxIPAddress =
                  @SandboxIPAddress
              AND u.SandboxOrganizationID IS NOT NULL
              AND ISNULL(
                  o.OrganizationStatusID,
                  -1
              ) = 0
              AND COALESCE(
                    activity.LastDonationDate,
                    o.DateTimeCreated,
                    u.DateTimeCreated,
                    SYSUTCDATETIME()
                  ) >= DATEADD(
                        DAY,
                        -@ActiveDays,
                        SYSUTCDATETIME()
                  )

            ORDER BY
                COALESCE(
                    activity.LastDonationDate,
                    o.DateTimeCreated,
                    u.DateTimeCreated
                ) DESC,

                u.UserID DESC;

            RETURN;
        END;

        UPDATE u
        SET
            SandboxIPAddress =
                NULL,

            DateTimeUpdated =
                SYSUTCDATETIME(),

            UpdatedByUserID =
                @CreatedByUserID

        FROM dbo.[User] u

        INNER JOIN dbo.Organization o
            ON o.EMAOrganizationID =
               u.SandboxOrganizationID

        OUTER APPLY
        (
            SELECT
                MAX(d.DateTimeReceived) AS LastDonationDate
            FROM dbo.Donation d
            WHERE d.EMAOrganizationID =
                  o.EMAOrganizationID
        ) activity

        WHERE u.SandboxIPAddress =
              @SandboxIPAddress
          AND u.SandboxOrganizationID IS NOT NULL
          AND ISNULL(
              o.OrganizationStatusID,
              -1
          ) = 0
          AND COALESCE(
                activity.LastDonationDate,
                o.DateTimeCreated,
                u.DateTimeCreated,
                '19000101'
              ) < DATEADD(
                    DAY,
                    -@ActiveDays,
                    SYSUTCDATETIME()
              );

        IF EXISTS
        (
            SELECT 1
            FROM dbo.[User] u WITH
            (
                UPDLOCK,
                HOLDLOCK
            )
            WHERE RIGHT(
                REPLACE(
                    REPLACE(
                        REPLACE(
                            REPLACE(
                                REPLACE(
                                    REPLACE(
                                        ISNULL(
                                            u.PhoneNumber,
                                            ''
                                        ),
                                        '(',
                                        ''
                                    ),
                                    ')',
                                    ''
                                ),
                                '-',
                                ''
                            ),
                            ' ',
                            ''
                        ),
                        '.',
                        ''
                    ),
                    '+',
                    ''
                ),
                10
            ) = @PhoneNumber
        )
        OR EXISTS
        (
            SELECT 1
            FROM dbo.Donor donor WITH
            (
                UPDLOCK,
                HOLDLOCK
            )
            WHERE RIGHT(
                REPLACE(
                    REPLACE(
                        REPLACE(
                            REPLACE(
                                REPLACE(
                                    REPLACE(
                                        ISNULL(
                                            donor.PhoneNumber,
                                            ''
                                        ),
                                        '(',
                                        ''
                                    ),
                                    ')',
                                    ''
                                ),
                                '-',
                                ''
                            ),
                            ' ',
                            ''
                        ),
                        '.',
                        ''
                    ),
                    '+',
                    ''
                ),
                10
            ) = @PhoneNumber
        )
            THROW 51004,
                'The generated sandbox phone number already exists.',
                1;

        IF EXISTS
        (
            SELECT 1
            FROM dbo.Organization
            WHERE LOWER(
                LTRIM(
                    RTRIM(
                        SandboxEtransferEmail
                    )
                )
            ) = @SandboxEmail
        )
            THROW 51004,
                'The generated sandbox email address already exists.',
                1;

        IF @TemplateUserID IS NULL
        BEGIN
            SELECT TOP (1)
                @TemplateUserID =
                    ou.UserID

            FROM dbo.OrganizationUser ou

            INNER JOIN dbo.[User] u
                ON u.UserID =
                   ou.UserID

            LEFT JOIN dbo.OrganizationUserRole ur
                ON ur.UserID =
                   u.UserID
               AND ur.EMAOrganizationID =
                   ou.EMAOrganizationID

            WHERE ou.EMAOrganizationID =
                  @TemplateOrganizationID
              AND u.IsActive = 1

            GROUP BY
                ou.UserID,
                u.UserName

            ORDER BY
                COUNT(
                    DISTINCT ur.RoleID
                ) DESC,

                ou.UserID;
        END;

        IF @TemplateUserID IS NULL
           OR NOT EXISTS
           (
               SELECT 1
               FROM dbo.OrganizationUser ou

               INNER JOIN dbo.[User] u
                   ON u.UserID =
                      ou.UserID

               WHERE ou.EMAOrganizationID =
                     @TemplateOrganizationID
                 AND ou.UserID =
                     @TemplateUserID
                 AND u.IsActive = 1
           )
            THROW 51008,
                'The sandbox template organization does not have a valid template user.',
                1;

        IF NOT EXISTS
        (
            SELECT 1
            FROM dbo.OrganizationUserRole
            WHERE UserID =
                  @TemplateUserID
              AND EMAOrganizationID =
                  @TemplateOrganizationID
        )
            THROW 51009,
                'The configured sandbox template user does not have any roles.',
                1;

        IF OBJECT_ID(
            'dbo.OrganizationStatus',
            'U'
        ) IS NOT NULL
           AND NOT EXISTS
           (
               SELECT 1
               FROM dbo.OrganizationStatus
               WHERE OrganizationStatusID = 0
           )
            THROW 51010,
                'OrganizationStatusID 0 must exist before sandbox organizations can be created.',
                1;

        CREATE TABLE #SandboxCloneMap
        (
            EntityName SYSNAME NOT NULL,
            OldID BIGINT NOT NULL,
            NewID BIGINT NOT NULL,

            CONSTRAINT PK_SandboxCloneMap
                PRIMARY KEY
                (
                    EntityName,
                    OldID
                )
        );

        CREATE TABLE #SandboxCloneQueue
        (
            QueueID INT IDENTITY(1, 1) NOT NULL
                CONSTRAINT PK_SandboxCloneQueue
                PRIMARY KEY,

            OldID BIGINT NOT NULL
        );

        DECLARE
            @OrganizationColumns NVARCHAR(MAX),
            @OrganizationSelectColumns NVARCHAR(MAX),
            @OrganizationInsertSql NVARCHAR(MAX),
            @NewOrganizationID INT;

        SELECT
            @OrganizationColumns =
                STRING_AGG(
                    CONVERT(
                        NVARCHAR(MAX),
                        QUOTENAME(c.name)
                    ),
                    N', '
                ) WITHIN GROUP
                (
                    ORDER BY c.column_id
                ),

            @OrganizationSelectColumns =
                STRING_AGG(
                    CONVERT(
                        NVARCHAR(MAX),

                        CASE
                            WHEN c.name =
                                 N'OrganizationName'
                                THEN
                                    N'CONCAT(
                                        N''Organization '',
                                        @PhoneNumber
                                    )'

                            WHEN c.name =
                                 N'OrganizationStatusID'
                                THEN
                                    N'0'

                            WHEN c.name =
                                 N'SandboxEtransferEmail'
                                THEN
                                    N'@SandboxEmail'

                            WHEN c.name =
                                 N'EtransferEmailAddress'
                                THEN
                                    N'NULL'

                            WHEN c.name =
                                 N'EtransferEmailVerified'
                                THEN
                                    N'''N'''

                            WHEN c.name IN
                            (
                                N'OrganizationPhone',
                                N'AdminPhone',
                                N'ContactPhone'
                            )
                                THEN
                                    N'@FormattedPhoneNumber'

                            WHEN c.name IN
                            (
                                N'AdminEmail',
                                N'ContactEmail',
                                N'OrganizationEmail'
                            )
                                THEN
                                    N'@SandboxEmail'

                            WHEN c.name IN
                            (
                                N'AdminEmailVerified',
                                N'AdminPhoneVerified'
                            )
                                THEN
                                    N'''N'''

                            WHEN c.name IN
                            (
                                N'EMADonorIDForAnonymous',
                                N'EMAEventIDForCreditDebitCards',
                                N'EMASourceIDForCreditDebitCards',
                                N'EMAMethodOfGivingIDForCreditDebitCards',
                                N'EMAFundIDForCreditDebitCards',
                                N'EMACampusIDForCreditDebitCards',
                                N'EMAEventIDForEtransfers',
                                N'EMASourceIDForEtransfers',
                                N'EMAMethodOfGivingIDForETransfers',
                                N'EMAFundIDForEtransfer',
                                N'EMACampusIDForEtransfers',
                                N'EMAEventIDForPADs',
                                N'EMASourceIDForPADs',
                                N'EMAMethodOfGivingIDForPADs',
                                N'EMAFundIDForPADs',
                                N'EMACampusIDForPADs',
                                N'EMAMethodOfGivingIDForCash',
                                N'EMAMethodOfGivingIDForCheques'
                            )
                                THEN
                                    N'NULL'

                            WHEN c.name IN
                            (
                                N'CRARegisteredCharityNumber',
                                N'ETLDonorDataBlobID',
                                N'ETLDonationDataBlobID'
                            )
                                THEN
                                    N'NULL'

                            WHEN c.name IN
                            (
                                N'DateTimeCreated',
                                N'DatetimeCreated',
                                N'DateTImeCreated',
                                N'DateCreated',
                                N'CreatedDate',
                                N'DateTimeUpdated',
                                N'DatetimeUpdated',
                                N'DateUpdated',
                                N'UpdatedDate'
                            )
                                THEN
                                    N'SYSUTCDATETIME()'

                            WHEN c.name IN
                            (
                                N'CreatedByUserID',
                                N'CreatedbyUserID',
                                N'CreatedBuyUserID',
                                N'CreateddbyUserID',
                                N'UserIDWhoCreated',
                                N'UserIDWhoCrearted',
                                N'UpdatedByUserID',
                                N'UpdatedbyUserID',
                                N'UserIDWhoUpdated'
                            )
                                THEN
                                    N'@CreatedByUserID'

                            ELSE
                                N'src.' +
                                QUOTENAME(c.name)
                        END
                    ),
                    N', '
                ) WITHIN GROUP
                (
                    ORDER BY c.column_id
                )

        FROM sys.columns c

        WHERE c.object_id =
              OBJECT_ID(
                  'dbo.Organization'
              )
          AND c.name <>
              N'EMAOrganizationID'
          AND c.is_identity = 0
          AND c.is_computed = 0
          AND c.system_type_id <> 189;

        DECLARE @FormattedPhoneNumber NVARCHAR(20) =
            CONCAT(
                SUBSTRING(
                    @PhoneNumber,
                    1,
                    3
                ),
                N'-',
                SUBSTRING(
                    @PhoneNumber,
                    4,
                    3
                ),
                N'-',
                SUBSTRING(
                    @PhoneNumber,
                    7,
                    4
                )
            );

        SET @OrganizationInsertSql =
            N'INSERT INTO dbo.Organization
              (
                  ' +
                  @OrganizationColumns +
                  N'
              )
              SELECT
                  ' +
                  @OrganizationSelectColumns +
                  N'
              FROM dbo.Organization src
              WHERE src.EMAOrganizationID =
                    @TemplateOrganizationID;

              SET @NewOrganizationID =
                  CONVERT(
                      INT,
                      SCOPE_IDENTITY()
                  );';

        EXEC sys.sp_executesql
            @OrganizationInsertSql,

            N'@TemplateOrganizationID INT,
              @PhoneNumber NVARCHAR(20),
              @FormattedPhoneNumber NVARCHAR(20),
              @SandboxEmail NVARCHAR(512),
              @CreatedByUserID INT,
              @NewOrganizationID INT OUTPUT',

            @TemplateOrganizationID =
                @TemplateOrganizationID,

            @PhoneNumber =
                @PhoneNumber,

            @FormattedPhoneNumber =
                @FormattedPhoneNumber,

            @SandboxEmail =
                @SandboxEmail,

            @CreatedByUserID =
                @CreatedByUserID,

            @NewOrganizationID =
                @NewOrganizationID OUTPUT;

        IF @NewOrganizationID IS NULL
            THROW 51011,
                'The sandbox organization insert did not return an OrganizationID.',
                1;

        DECLARE @DonationDateShiftDays INT = 0;

        SELECT
            @DonationDateShiftDays =
                CASE
                    WHEN MAX(
                        DateTimeReceived
                    ) IS NULL
                        THEN 0

                    ELSE
                        DATEDIFF(
                            DAY,
                            MAX(
                                DateTimeReceived
                            ),
                            SYSUTCDATETIME()
                        )
                END

        FROM dbo.Donation

        WHERE EMAOrganizationID =
              @TemplateOrganizationID;

        EXEC dbo.sp_SandboxCloneIdentityTable
            @EntityName =
                N'Campus',

            @TableName =
                N'OrganizationCampus',

            @IdentityColumn =
                N'EMACampusID',

            @OrganizationColumn =
                N'EMAOrganizationID',

            @SourceOrganizationID =
                @TemplateOrganizationID,

            @TargetOrganizationID =
                @NewOrganizationID,

            @CreatedByUserID =
                @CreatedByUserID,

            @PhoneNumber =
                @PhoneNumber,

            @SandboxEmail =
                @SandboxEmail;

        EXEC dbo.sp_SandboxCloneIdentityTable
            @EntityName =
                N'Method',

            @TableName =
                N'OrganizationMethodOfGiving',

            @IdentityColumn =
                N'EMAMethodOfGivingID',

            @OrganizationColumn =
                N'EMAOrganizationID',

            @SourceOrganizationID =
                @TemplateOrganizationID,

            @TargetOrganizationID =
                @NewOrganizationID,

            @CreatedByUserID =
                @CreatedByUserID,

            @PhoneNumber =
                @PhoneNumber,

            @SandboxEmail =
                @SandboxEmail;

        EXEC dbo.sp_SandboxCloneIdentityTable
            @EntityName =
                N'Event',

            @TableName =
                N'OrganizationEvent',

            @IdentityColumn =
                N'EMAEventID',

            @OrganizationColumn =
                N'EMAOrganizationID',

            @SourceOrganizationID =
                @TemplateOrganizationID,

            @TargetOrganizationID =
                @NewOrganizationID,

            @CreatedByUserID =
                @CreatedByUserID,

            @PhoneNumber =
                @PhoneNumber,

            @SandboxEmail =
                @SandboxEmail;

        EXEC dbo.sp_SandboxCloneIdentityTable
            @EntityName =
                N'DonationSource',

            @TableName =
                N'OrganizationDonationSource',

            @IdentityColumn =
                N'EMADonationSourceID',

            @OrganizationColumn =
                N'EMAOrganizationID',

            @SourceOrganizationID =
                @TemplateOrganizationID,

            @TargetOrganizationID =
                @NewOrganizationID,

            @CreatedByUserID =
                @CreatedByUserID,

            @PhoneNumber =
                @PhoneNumber,

            @SandboxEmail =
                @SandboxEmail;

        EXEC dbo.sp_SandboxCloneIdentityTable
            @EntityName =
                N'TributeType',

            @TableName =
                N'OrganizationTributeType',

            @IdentityColumn =
                N'EMATributeTypeID',

            @OrganizationColumn =
                N'EMAOrganizationID',

            @SourceOrganizationID =
                @TemplateOrganizationID,

            @TargetOrganizationID =
                @NewOrganizationID,

            @CreatedByUserID =
                @CreatedByUserID,

            @PhoneNumber =
                @PhoneNumber,

            @SandboxEmail =
                @SandboxEmail;

        EXEC dbo.sp_SandboxCloneIdentityTable
            @EntityName =
                N'Fund',

            @TableName =
                N'OrganizationFund',

            @IdentityColumn =
                N'EMAFundID',

            @OrganizationColumn =
                N'EMAOrganizationID',

            @SourceOrganizationID =
                @TemplateOrganizationID,

            @TargetOrganizationID =
                @NewOrganizationID,

            @CreatedByUserID =
                @CreatedByUserID,

            @PhoneNumber =
                @PhoneNumber,

            @SandboxEmail =
                @SandboxEmail;

        EXEC dbo.sp_SandboxCloneIdentityTable
            @EntityName =
                N'DonorStatus',

            @TableName =
                N'DonorStatus',

            @IdentityColumn =
                N'DonorStatusID',

            @OrganizationColumn =
                N'EMAOrganizationId',

            @SourceOrganizationID =
                @TemplateOrganizationID,

            @TargetOrganizationID =
                @NewOrganizationID,

            @CreatedByUserID =
                @CreatedByUserID,

            @PhoneNumber =
                @PhoneNumber,

            @SandboxEmail =
                @SandboxEmail;

        EXEC dbo.sp_SandboxCloneIdentityTable
            @EntityName =
                N'DonorActivityType',

            @TableName =
                N'DonorJourneyActivityType',

            @IdentityColumn =
                N'ActivityTypeID',

            @OrganizationColumn =
                N'EMAOrganizationID',

            @SourceOrganizationID =
                @TemplateOrganizationID,

            @TargetOrganizationID =
                @NewOrganizationID,

            @CreatedByUserID =
                @CreatedByUserID,

            @PhoneNumber =
                @PhoneNumber,

            @SandboxEmail =
                @SandboxEmail;

        EXEC dbo.sp_SandboxCloneIdentityTable
            @EntityName =
                N'Donor',

            @TableName =
                N'Donor',

            @IdentityColumn =
                N'EMADonorID',

            @OrganizationColumn =
                N'EMAOrganizationID',

            @SourceOrganizationID =
                @TemplateOrganizationID,

            @TargetOrganizationID =
                @NewOrganizationID,

            @CreatedByUserID =
                @CreatedByUserID,

            @PhoneNumber =
                @PhoneNumber,

            @SandboxEmail =
                @SandboxEmail;

        EXEC dbo.sp_SandboxCloneIdentityTable
            @EntityName =
                N'Donation',

            @TableName =
                N'Donation',

            @IdentityColumn =
                N'EMADonationID',

            @OrganizationColumn =
                N'EMAOrganizationID',

            @SourceOrganizationID =
                @TemplateOrganizationID,

            @TargetOrganizationID =
                @NewOrganizationID,

            @CreatedByUserID =
                @CreatedByUserID,

            @PhoneNumber =
                @PhoneNumber,

            @SandboxEmail =
                @SandboxEmail,

            @DonationDateShiftDays =
                @DonationDateShiftDays,

            @MaxRows =
                @MaxTemplateDonations,

            @OrderByClause =
                N'ISNULL(
                    src.DateTimeReceived,
                    src.DateTimeCreated
                ) DESC,
                src.EMADonationID DESC';

        INSERT INTO dbo.DonationDistribution
        (
            EMADonationID,
            EMACampusID,
            EMAFundID,
            DistributionAmount
        )
        SELECT
            CONVERT(
                BIGINT,
                donationMap.NewID
            ),

            CONVERT(
                INT,
                campusMap.NewID
            ),

            CONVERT(
                INT,
                fundMap.NewID
            ),

            distribution.DistributionAmount

        FROM dbo.DonationDistribution distribution

        INNER JOIN #SandboxCloneMap donationMap
            ON donationMap.EntityName =
               N'Donation'
           AND donationMap.OldID =
               CONVERT(
                   BIGINT,
                   distribution.EMADonationID
               )

        INNER JOIN #SandboxCloneMap campusMap
            ON campusMap.EntityName =
               N'Campus'
           AND campusMap.OldID =
               CONVERT(
                   BIGINT,
                   distribution.EMACampusID
               )

        INNER JOIN #SandboxCloneMap fundMap
            ON fundMap.EntityName =
               N'Fund'
           AND fundMap.OldID =
               CONVERT(
                   BIGINT,
                   distribution.EMAFundID
               );

        IF COL_LENGTH(
            'dbo.OrganizationCampus',
            'DefaultCampusFundID'
        ) IS NOT NULL
        BEGIN
            UPDATE targetCampus

            SET
                targetCampus.DefaultCampusFundID =
                    CONVERT(
                        INT,
                        targetFundMap.NewID
                    )

            FROM dbo.OrganizationCampus sourceCampus

            INNER JOIN #SandboxCloneMap campusMap
                ON campusMap.EntityName =
                   N'Campus'
               AND campusMap.OldID =
                   CONVERT(
                       BIGINT,
                       sourceCampus.EMACampusID
                   )

            INNER JOIN dbo.OrganizationCampus targetCampus
                ON targetCampus.EMACampusID =
                   CONVERT(
                       INT,
                       campusMap.NewID
                   )

            LEFT JOIN #SandboxCloneMap targetFundMap
                ON targetFundMap.EntityName =
                   N'Fund'
               AND targetFundMap.OldID =
                   CONVERT(
                       BIGINT,
                       sourceCampus.DefaultCampusFundID
                   )

            WHERE sourceCampus.EMAOrganizationID =
                  @TemplateOrganizationID;
        END;

        UPDATE targetOrganization
        SET
            EMADonorIDForAnonymous =
                CONVERT(INT, anonymousDonorMap.NewID),

            EMAEventIDForCreditDebitCards =
                CONVERT(INT, creditDebitEventMap.NewID),

            EMASourceIDForCreditDebitCards =
                CONVERT(INT, creditDebitSourceMap.NewID),

            EMAMethodOfGivingIDForCreditDebitCards =
                CONVERT(INT, creditDebitMethodMap.NewID),

            EMAFundIDForCreditDebitCards =
                CONVERT(INT, creditDebitFundMap.NewID),

            EMACampusIDForCreditDebitCards =
                CONVERT(INT, creditDebitCampusMap.NewID),

            EMAEventIDForEtransfers =
                CONVERT(INT, etransferEventMap.NewID),

            EMASourceIDForEtransfers =
                CONVERT(INT, etransferSourceMap.NewID),

            EMAMethodOfGivingIDForETransfers =
                CONVERT(INT, etransferMethodMap.NewID),

            EMAFundIDForEtransfer =
                CONVERT(INT, etransferFundMap.NewID),

            EMACampusIDForEtransfers =
                CONVERT(INT, etransferCampusMap.NewID),

            EMAEventIDForPADs =
                CONVERT(INT, padEventMap.NewID),

            EMASourceIDForPADs =
                CONVERT(INT, padSourceMap.NewID),

            EMAMethodOfGivingIDForPADs =
                CONVERT(INT, padMethodMap.NewID),

            EMAFundIDForPADs =
                CONVERT(INT, padFundMap.NewID),

            EMACampusIDForPADs =
                CONVERT(INT, padCampusMap.NewID),

            EMAMethodOfGivingIDForCash =
                CONVERT(INT, cashMethodMap.NewID),

            EMAMethodOfGivingIDForCheques =
                CONVERT(INT, chequeMethodMap.NewID)

        FROM dbo.Organization targetOrganization

        CROSS JOIN dbo.Organization sourceOrganization

        LEFT JOIN #SandboxCloneMap anonymousDonorMap
            ON anonymousDonorMap.EntityName = N'Donor'
           AND anonymousDonorMap.OldID =
               CONVERT(BIGINT, sourceOrganization.EMADonorIDForAnonymous)

        LEFT JOIN #SandboxCloneMap creditDebitEventMap
            ON creditDebitEventMap.EntityName = N'Event'
           AND creditDebitEventMap.OldID =
               CONVERT(BIGINT, sourceOrganization.EMAEventIDForCreditDebitCards)

        LEFT JOIN #SandboxCloneMap creditDebitSourceMap
            ON creditDebitSourceMap.EntityName = N'DonationSource'
           AND creditDebitSourceMap.OldID =
               CONVERT(BIGINT, sourceOrganization.EMASourceIDForCreditDebitCards)

        LEFT JOIN #SandboxCloneMap creditDebitMethodMap
            ON creditDebitMethodMap.EntityName = N'Method'
           AND creditDebitMethodMap.OldID =
               CONVERT(BIGINT, sourceOrganization.EMAMethodOfGivingIDForCreditDebitCards)

        LEFT JOIN #SandboxCloneMap creditDebitFundMap
            ON creditDebitFundMap.EntityName = N'Fund'
           AND creditDebitFundMap.OldID =
               CONVERT(BIGINT, sourceOrganization.EMAFundIDForCreditDebitCards)

        LEFT JOIN #SandboxCloneMap creditDebitCampusMap
            ON creditDebitCampusMap.EntityName = N'Campus'
           AND creditDebitCampusMap.OldID =
               CONVERT(BIGINT, sourceOrganization.EMACampusIDForCreditDebitCards)

        LEFT JOIN #SandboxCloneMap etransferEventMap
            ON etransferEventMap.EntityName = N'Event'
           AND etransferEventMap.OldID =
               CONVERT(BIGINT, sourceOrganization.EMAEventIDForEtransfers)

        LEFT JOIN #SandboxCloneMap etransferSourceMap
            ON etransferSourceMap.EntityName = N'DonationSource'
           AND etransferSourceMap.OldID =
               CONVERT(BIGINT, sourceOrganization.EMASourceIDForEtransfers)

        LEFT JOIN #SandboxCloneMap etransferMethodMap
            ON etransferMethodMap.EntityName = N'Method'
           AND etransferMethodMap.OldID =
               CONVERT(BIGINT, sourceOrganization.EMAMethodOfGivingIDForETransfers)

        LEFT JOIN #SandboxCloneMap etransferFundMap
            ON etransferFundMap.EntityName = N'Fund'
           AND etransferFundMap.OldID =
               CONVERT(BIGINT, sourceOrganization.EMAFundIDForEtransfer)

        LEFT JOIN #SandboxCloneMap etransferCampusMap
            ON etransferCampusMap.EntityName = N'Campus'
           AND etransferCampusMap.OldID =
               CONVERT(BIGINT, sourceOrganization.EMACampusIDForEtransfers)

        LEFT JOIN #SandboxCloneMap padEventMap
            ON padEventMap.EntityName = N'Event'
           AND padEventMap.OldID =
               CONVERT(BIGINT, sourceOrganization.EMAEventIDForPADs)

        LEFT JOIN #SandboxCloneMap padSourceMap
            ON padSourceMap.EntityName = N'DonationSource'
           AND padSourceMap.OldID =
               CONVERT(BIGINT, sourceOrganization.EMASourceIDForPADs)

        LEFT JOIN #SandboxCloneMap padMethodMap
            ON padMethodMap.EntityName = N'Method'
           AND padMethodMap.OldID =
               CONVERT(BIGINT, sourceOrganization.EMAMethodOfGivingIDForPADs)

        LEFT JOIN #SandboxCloneMap padFundMap
            ON padFundMap.EntityName = N'Fund'
           AND padFundMap.OldID =
               CONVERT(BIGINT, sourceOrganization.EMAFundIDForPADs)

        LEFT JOIN #SandboxCloneMap padCampusMap
            ON padCampusMap.EntityName = N'Campus'
           AND padCampusMap.OldID =
               CONVERT(BIGINT, sourceOrganization.EMACampusIDForPADs)

        LEFT JOIN #SandboxCloneMap cashMethodMap
            ON cashMethodMap.EntityName = N'Method'
           AND cashMethodMap.OldID =
               CONVERT(BIGINT, sourceOrganization.EMAMethodOfGivingIDForCash)

        LEFT JOIN #SandboxCloneMap chequeMethodMap
            ON chequeMethodMap.EntityName = N'Method'
           AND chequeMethodMap.OldID =
               CONVERT(BIGINT, sourceOrganization.EMAMethodOfGivingIDForCheques)

        WHERE targetOrganization.EMAOrganizationID =
              @NewOrganizationID
          AND sourceOrganization.EMAOrganizationID =
              @TemplateOrganizationID;

        EXEC dbo.sp_SandboxCloneDirectTable
            @TableName =
                N'OrganizationEntityAliase',

            @OrganizationColumn =
                N'EMAOrganizationID',

            @SourceOrganizationID =
                @TemplateOrganizationID,

            @TargetOrganizationID =
                @NewOrganizationID,

            @CreatedByUserID =
                @CreatedByUserID;

        EXEC dbo.sp_SandboxCloneDirectTable
            @TableName =
                N'OrganizationDashboard',

            @OrganizationColumn =
                N'EMAOrganizationID',

            @SourceOrganizationID =
                @TemplateOrganizationID,

            @TargetOrganizationID =
                @NewOrganizationID,

            @CreatedByUserID =
                @CreatedByUserID;

        DECLARE
            @SandboxUserEmail NVARCHAR(255) =
                CONCAT(
                    N'sandbox-user-',
                    @PhoneNumber,
                    N'@example.invalid'
                ),

            @NewUserID INT,
            @NewOrganizationUserID INT,
            @OrganizationUserStatusID SMALLINT;

        INSERT INTO dbo.[User]
        (
            UserName,
            EmailAddress,
            EmailAddressValidated,
            PhoneNumber,
            SandboxOrganizationID,
            SandboxIPAddress,
            IsActive,
            CreatedByUserID,
            DateTimeCreated,
            UpdatedByUserID,
            DateTimeUpdated
        )
        VALUES
        (
            CONCAT(
                N'Sandbox User ',
                @PhoneNumber
            ),

            @SandboxUserEmail,
            'N',
            @FormattedPhoneNumber,
            @NewOrganizationID,
            @SandboxIPAddress,
            1,
            @CreatedByUserID,
            SYSUTCDATETIME(),
            @CreatedByUserID,
            SYSUTCDATETIME()
        );

        SET @NewUserID =
            CONVERT(
                INT,
                SCOPE_IDENTITY()
            );

        SELECT
            @OrganizationUserStatusID =
                ou.OrganizationUserStatusID

        FROM dbo.OrganizationUser ou

        WHERE ou.EMAOrganizationID =
              @TemplateOrganizationID
          AND ou.UserID =
              @TemplateUserID;

        IF @OrganizationUserStatusID IS NULL
            THROW 51013,
                'The sandbox template user does not have an OrganizationUserStatusID.',
                1;

        SELECT
            @NewOrganizationUserID =
                NEXT VALUE FOR
                    dbo.OrganizationUserIDSequence;

        INSERT INTO dbo.OrganizationUser
        (
            OrganizationUserID,
            EMAOrganizationID,
            UserID,
            OrganizationUserStatusID
        )
        VALUES
        (
            @NewOrganizationUserID,
            @NewOrganizationID,
            @NewUserID,
            @OrganizationUserStatusID
        );

        INSERT INTO dbo.OrganizationUserRole
        (
            EMAOrganizationID,
            UserID,
            RoleID
        )
        SELECT DISTINCT
            @NewOrganizationID,
            @NewUserID,
            ur.RoleID

        FROM dbo.OrganizationUserRole ur

        INNER JOIN dbo.[Role] r
            ON r.RoleID =
               ur.RoleID

        WHERE ur.UserID =
              @TemplateUserID
          AND ur.EMAOrganizationID =
              @TemplateOrganizationID
          AND ISNULL(
              r.IsActive,
              'Y'
          ) = 'Y';

        IF NOT EXISTS
        (
            SELECT 1
            FROM dbo.OrganizationUserRole
            WHERE UserID =
                  @NewUserID
              AND EMAOrganizationID =
                  @NewOrganizationID
        )
            THROW 51012,
                'No active roles were copied to the sandbox user.',
                1;

        /* -----------------------------------------------------------------
           Journey history
           ----------------------------------------------------------------- */

        INSERT INTO dbo.DonorJourney
        (
            EMAOrganizationID,
            EMADonorId,
            UserId,
            DonorInteractionId,
            ActivityDateTIme,
            ActivityTypeID,
            ActivityComments,
            DonorStatusID,
            FollowUpDateTIme,
            FollowUpActivityTypeID
        )
        SELECT
            @NewOrganizationID,
            CONVERT(
                INT,
                donorMap.NewID
            ),
            @NewUserID,
            LEFT
            (
                CONVERT
                (
                    VARCHAR(64),
                    HASHBYTES
                    (
                        'SHA2_256',
                        CONCAT
                        (
                            @NewOrganizationID,
                            ':',
                            donorMap.NewID,
                            ':',
                            RTRIM(
                                journey.DonorInteractionId
                            )
                        )
                    ),
                    2
                ),
                10
            ),
            DATEADD(
                DAY,
                @DonationDateShiftDays,
                journey.ActivityDateTIme
            ),
            CONVERT
            (
                INT,
                activityTypeMap.NewID
            ),
            journey.ActivityComments,
            CONVERT
            (
                SMALLINT,
                donorStatusMap.NewID
            ),
            CASE
                WHEN journey.FollowUpDateTIme IS NULL
                    THEN NULL
                ELSE DATEADD(
                    DAY,
                    @DonationDateShiftDays,
                    journey.FollowUpDateTIme
                )
            END,
            CASE
                WHEN journey.FollowUpActivityTypeID IS NULL
                    THEN NULL
                ELSE CONVERT
                     (
                         SMALLINT,
                         followUpActivityTypeMap.NewID
                     )
            END
        FROM dbo.DonorJourney journey
        INNER JOIN #SandboxCloneMap donorMap
            ON donorMap.EntityName =
               N'Donor'
           AND donorMap.OldID =
               CONVERT(
                   BIGINT,
                   journey.EMADonorId
               )
        INNER JOIN #SandboxCloneMap donorStatusMap
            ON donorStatusMap.EntityName =
               N'DonorStatus'
           AND donorStatusMap.OldID =
               CONVERT(
                   BIGINT,
                   journey.DonorStatusID
               )
        INNER JOIN #SandboxCloneMap activityTypeMap
            ON activityTypeMap.EntityName =
               N'DonorActivityType'
           AND activityTypeMap.OldID =
               CONVERT(
                   BIGINT,
                   journey.ActivityTypeID
               )
        LEFT JOIN #SandboxCloneMap followUpActivityTypeMap
            ON followUpActivityTypeMap.EntityName =
               N'DonorActivityType'
           AND followUpActivityTypeMap.OldID =
               CONVERT(
                   BIGINT,
                   journey.FollowUpActivityTypeID
               )
        WHERE journey.EMAOrganizationID =
              @TemplateOrganizationID;

        INSERT INTO dbo.OrganizationJourney
        (
            EMAOrganizationID,
            EMAUserID,
            ActivityDateTime,
            ActivityTypeID,
            InteractionComments,
            OrganizationStatusID,
            FollowUpDateTime,
            FollowUpActivityTypeID,
            FollowUpUserId
        )
        SELECT
            @NewOrganizationID,
            @NewUserID,
            DATEADD(
                DAY,
                @DonationDateShiftDays,
                journey.ActivityDateTime
            ),
            journey.ActivityTypeID,
            journey.InteractionComments,
            journey.OrganizationStatusID,
            CASE
                WHEN journey.FollowUpDateTime IS NULL
                    THEN NULL
                ELSE DATEADD(
                    DAY,
                    @DonationDateShiftDays,
                    journey.FollowUpDateTime
                )
            END,
            journey.FollowUpActivityTypeID,
            CASE
                WHEN journey.FollowUpUserId IS NULL
                    THEN NULL
                ELSE @NewUserID
            END
        FROM dbo.OrganizationJourney journey
        WHERE journey.EMAOrganizationID =
              @TemplateOrganizationID;

        IF
        (
            SELECT COUNT(*)
            FROM dbo.DonorStatus
            WHERE EMAOrganizationId =
                  @NewOrganizationID
        ) <>
        (
            SELECT COUNT(*)
            FROM dbo.DonorStatus
            WHERE EMAOrganizationId =
                  @TemplateOrganizationID
        )
            THROW 51014,
                'The sandbox donor statuses were not cloned completely.',
                1;

        IF
        (
            SELECT COUNT(*)
            FROM dbo.DonorJourneyActivityType
            WHERE EMAOrganizationID =
                  @NewOrganizationID
        ) <>
        (
            SELECT COUNT(*)
            FROM dbo.DonorJourneyActivityType
            WHERE EMAOrganizationID =
                  @TemplateOrganizationID
        )
            THROW 51015,
                'The sandbox donor journey activity types were not cloned completely.',
                1;

        IF
        (
            SELECT COUNT(*)
            FROM dbo.DonorJourney
            WHERE EMAOrganizationID =
                  @NewOrganizationID
        ) <>
        (
            SELECT COUNT(*)
            FROM dbo.DonorJourney
            WHERE EMAOrganizationID =
                  @TemplateOrganizationID
        )
            THROW 51016,
                'The sandbox donor journeys were not cloned completely.',
                1;

        IF
        (
            SELECT COUNT(*)
            FROM dbo.OrganizationJourney
            WHERE EMAOrganizationID =
                  @NewOrganizationID
        ) <>
        (
            SELECT COUNT(*)
            FROM dbo.OrganizationJourney
            WHERE EMAOrganizationID =
                  @TemplateOrganizationID
        )
            THROW 51017,
                'The sandbox organization journeys were not cloned completely.',
                1;

        COMMIT TRANSACTION;

        SELECT
            @NewOrganizationID AS SandboxOrganizationID,
            @NewUserID AS SandboxUserID,
            organization.OrganizationName,
            sandboxUser.PhoneNumber,
            organization.SandboxEtransferEmail AS SandboxEmailAddress,
            organization.DateTimeCreated,
            activity.LastDonationDate

        FROM dbo.Organization organization

        INNER JOIN dbo.[User] sandboxUser
            ON sandboxUser.UserID =
               @NewUserID

        OUTER APPLY
        (
            SELECT
                MAX(d.DateTimeReceived) AS LastDonationDate
            FROM dbo.Donation d
            WHERE d.EMAOrganizationID =
                  @NewOrganizationID
        ) activity

        WHERE organization.EMAOrganizationID =
              @NewOrganizationID;
    END TRY

    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;

        THROW;
    END CATCH;
END;
GO

/* -------------------------------------------------------------------------
   Delete one sandbox and its known child graph in FK-safe order.
   ------------------------------------------------------------------------- */

CREATE OR ALTER PROCEDURE dbo.sp_SandboxDeleteOne
    @SandboxOrganizationID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @SandboxOrganizationID <= 0
        THROW 51020,
            'A valid sandbox OrganizationID is required.',
            1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.Organization
        WHERE EMAOrganizationID =
              @SandboxOrganizationID
          AND ISNULL(
              OrganizationStatusID,
              -1
          ) = 0
    )
        THROW 51021,
            'The requested organization is not a sandbox organization.',
            1;

    BEGIN TRANSACTION;

    BEGIN TRY
        CREATE TABLE #SandboxUsers
        (
            UserID INT NOT NULL
                PRIMARY KEY
        );

        INSERT INTO #SandboxUsers
        (
            UserID
        )
        SELECT
            u.UserID
        FROM dbo.[User] u
        WHERE u.SandboxOrganizationID =
              @SandboxOrganizationID;

        CREATE TABLE #SandboxDonors
        (
            EMADonorID INT NOT NULL
                PRIMARY KEY
        );

        INSERT INTO #SandboxDonors
        (
            EMADonorID
        )
        SELECT
            d.EMADonorID
        FROM dbo.Donor d
        WHERE d.EMAOrganizationID =
              @SandboxOrganizationID;

        CREATE TABLE #SandboxDonations
        (
            EMADonationID BIGINT NOT NULL
                PRIMARY KEY
        );

        INSERT INTO #SandboxDonations
        (
            EMADonationID
        )
        SELECT
            d.EMADonationID
        FROM dbo.Donation d
        WHERE d.EMAOrganizationID =
              @SandboxOrganizationID;

        CREATE TABLE #SandboxReceipts
        (
            EMAReceiptID BIGINT NOT NULL
                PRIMARY KEY
        );

        IF OBJECT_ID(
            'dbo.DonationReceipt',
            'U'
        ) IS NOT NULL
           AND COL_LENGTH(
               'dbo.Donation',
               'EMAReceiptID'
           ) IS NOT NULL
        BEGIN
            INSERT INTO #SandboxReceipts
            (
                EMAReceiptID
            )
            SELECT DISTINCT
                CONVERT(
                    BIGINT,
                    d.EMAReceiptID
                )
            FROM dbo.Donation d
            WHERE d.EMAOrganizationID =
                  @SandboxOrganizationID
              AND d.EMAReceiptID IS NOT NULL;
        END;

        UPDATE dbo.Organization
        SET
            EMADonorIDForAnonymous = NULL,

            EMAEventIDForCreditDebitCards = NULL,
            EMASourceIDForCreditDebitCards = NULL,
            EMAMethodOfGivingIDForCreditDebitCards = NULL,
            EMAFundIDForCreditDebitCards = NULL,
            EMACampusIDForCreditDebitCards = NULL,

            EMAEventIDForEtransfers = NULL,
            EMASourceIDForEtransfers = NULL,
            EMAMethodOfGivingIDForETransfers = NULL,
            EMAFundIDForEtransfer = NULL,
            EMACampusIDForEtransfers = NULL,

            EMAEventIDForPADs = NULL,
            EMASourceIDForPADs = NULL,
            EMAMethodOfGivingIDForPADs = NULL,
            EMAFundIDForPADs = NULL,
            EMACampusIDForPADs = NULL,

            EMAMethodOfGivingIDForCash = NULL,
            EMAMethodOfGivingIDForCheques = NULL

        WHERE EMAOrganizationID =
              @SandboxOrganizationID;

        IF COL_LENGTH(
            'dbo.OrganizationCampus',
            'DefaultCampusFundID'
        ) IS NOT NULL
        BEGIN
            UPDATE dbo.OrganizationCampus
            SET DefaultCampusFundID =
                NULL
            WHERE EMAOrganizationID =
                  @SandboxOrganizationID;
        END;

        IF OBJECT_ID(
            'dbo.EmailSendLog',
            'U'
        ) IS NOT NULL
            DELETE log
            FROM dbo.EmailSendLog log

            INNER JOIN #SandboxDonations d
                ON d.EMADonationID =
                   log.EMADonationID;

        IF OBJECT_ID(
            'dbo.DonationStepLog',
            'U'
        ) IS NOT NULL
            DELETE log
            FROM dbo.DonationStepLog log

            INNER JOIN #SandboxDonations d
                ON d.EMADonationID =
                   log.EMADonationID;

        IF OBJECT_ID(
            'dbo.DonationAudit',
            'U'
        ) IS NOT NULL
            DELETE audit
            FROM dbo.DonationAudit audit

            INNER JOIN #SandboxDonations d
                ON d.EMADonationID =
                   audit.EMADonationID;

        IF OBJECT_ID(
            'dbo.DonationWIP',
            'U'
        ) IS NOT NULL
            DELETE wip
            FROM dbo.DonationWIP wip

            INNER JOIN #SandboxDonations d
                ON d.EMADonationID =
                   wip.EMADonationID;

        IF OBJECT_ID(
            'dbo.DonationDistribution',
            'U'
        ) IS NOT NULL
            DELETE distribution
            FROM dbo.DonationDistribution distribution

            INNER JOIN #SandboxDonations d
                ON d.EMADonationID =
                   distribution.EMADonationID;

        IF COL_LENGTH(
            'dbo.Donation',
            'EMAReceiptID'
        ) IS NOT NULL
            UPDATE dbo.Donation
            SET EMAReceiptID =
                NULL
            WHERE EMAOrganizationID =
                  @SandboxOrganizationID;

        DELETE FROM dbo.Donation
        WHERE EMAOrganizationID =
              @SandboxOrganizationID;

        IF OBJECT_ID(
            'dbo.DonationReceipt',
            'U'
        ) IS NOT NULL
            DELETE receipt
            FROM dbo.DonationReceipt receipt

            INNER JOIN #SandboxReceipts r
                ON r.EMAReceiptID =
                   receipt.EMAReceiptID;

        IF OBJECT_ID(
            'dbo.DonorJourney',
            'U'
        ) IS NOT NULL
            DELETE journey
            FROM dbo.DonorJourney journey

            INNER JOIN #SandboxDonors donor
                ON donor.EMADonorID =
                   journey.EMADonorID;

        IF OBJECT_ID(
            'dbo.MailingListDonor',
            'U'
        ) IS NOT NULL
            DELETE listDonor
            FROM dbo.MailingListDonor listDonor

            INNER JOIN #SandboxDonors donor
                ON donor.EMADonorID =
                   listDonor.EMADonorID;

        IF OBJECT_ID(
            'dbo.OrganizationListDonor',
            'U'
        ) IS NOT NULL
            DELETE listDonor
            FROM dbo.OrganizationListDonor listDonor

            INNER JOIN #SandboxDonors donor
                ON donor.EMADonorID =
                   listDonor.EMADonorID;

        DELETE FROM dbo.Donor
        WHERE EMAOrganizationID =
              @SandboxOrganizationID;

        IF OBJECT_ID(
            'dbo.MailingListIInstanceCriteria',
            'U'
        ) IS NOT NULL
        BEGIN
            DELETE criteria
            FROM dbo.MailingListIInstanceCriteria criteria

            INNER JOIN dbo.MailingListInstance instance
                ON instance.MailingListInstanceID =
                   criteria.MailingListInstanceID

            INNER JOIN dbo.MailingList list
                ON list.MailingListID =
                   instance.MailingListID

            WHERE list.EMAOrganizationID =
                  @SandboxOrganizationID;
        END;

        IF OBJECT_ID(
            'dbo.MailingListInstanceDonor',
            'U'
        ) IS NOT NULL
        BEGIN
            DELETE instanceDonor
            FROM dbo.MailingListInstanceDonor instanceDonor

            INNER JOIN dbo.MailingListInstance instance
                ON instance.MailingListInstanceID =
                   instanceDonor.MailingListInstanceID

            INNER JOIN dbo.MailingList list
                ON list.MailingListID =
                   instance.MailingListID

            WHERE list.EMAOrganizationID =
                  @SandboxOrganizationID;
        END;

        IF OBJECT_ID(
            'dbo.MailingListInstance',
            'U'
        ) IS NOT NULL
        BEGIN
            DELETE instance
            FROM dbo.MailingListInstance instance

            INNER JOIN dbo.MailingList list
                ON list.MailingListID =
                   instance.MailingListID

            WHERE list.EMAOrganizationID =
                  @SandboxOrganizationID;
        END;

        IF OBJECT_ID(
            'dbo.MailingListDonor',
            'U'
        ) IS NOT NULL
            DELETE FROM dbo.MailingListDonor
            WHERE EMAOrganizationID =
                  @SandboxOrganizationID;

        IF OBJECT_ID(
            'dbo.MailingList',
            'U'
        ) IS NOT NULL
            DELETE FROM dbo.MailingList
            WHERE EMAOrganizationID =
                  @SandboxOrganizationID;

        IF OBJECT_ID(
            'dbo.MailingListSender',
            'U'
        ) IS NOT NULL
            DELETE FROM dbo.MailingListSender
            WHERE OrganizationID =
                  @SandboxOrganizationID;

        IF OBJECT_ID(
            'dbo.OrganizationListDonor',
            'U'
        ) IS NOT NULL
           AND OBJECT_ID(
               'dbo.OrganizationList',
               'U'
           ) IS NOT NULL
           AND OBJECT_ID(
               'dbo.OrganizationListGroup',
               'U'
           ) IS NOT NULL
        BEGIN
            DELETE listDonor
            FROM dbo.OrganizationListDonor listDonor

            INNER JOIN dbo.OrganizationList list
                ON list.ListID =
                   listDonor.ListID

            INNER JOIN dbo.OrganizationListGroup listGroup
                ON listGroup.ListGroupID =
                   list.ListGroupID

            WHERE listGroup.EMAOrganizationID =
                  @SandboxOrganizationID;

            DELETE list
            FROM dbo.OrganizationList list

            INNER JOIN dbo.OrganizationListGroup listGroup
                ON listGroup.ListGroupID =
                   list.ListGroupID

            WHERE listGroup.EMAOrganizationID =
                  @SandboxOrganizationID;

            DELETE FROM dbo.OrganizationListGroup
            WHERE EMAOrganizationID =
                  @SandboxOrganizationID;
        END;

        IF OBJECT_ID(
            'dbo.OrganizationJourney',
            'U'
        ) IS NOT NULL
            DELETE FROM dbo.OrganizationJourney
            WHERE EMAOrganizationID =
                  @SandboxOrganizationID;

        IF OBJECT_ID(
            'dbo.InvoiceDetail',
            'U'
        ) IS NOT NULL
            DELETE FROM dbo.InvoiceDetail
            WHERE EMAOrganizationID =
                  @SandboxOrganizationID;

        IF OBJECT_ID(
            'dbo.OrganizationAudit',
            'U'
        ) IS NOT NULL
            DELETE FROM dbo.OrganizationAudit
            WHERE EMAOrganizationID =
                  @SandboxOrganizationID;

        IF OBJECT_ID(
            'dbo.OrganizationDashboard',
            'U'
        ) IS NOT NULL
            DELETE FROM dbo.OrganizationDashboard
            WHERE EMAOrganizationID =
                  @SandboxOrganizationID;

        IF OBJECT_ID(
            'dbo.OrganizationEntityAliase',
            'U'
        ) IS NOT NULL
            DELETE FROM dbo.OrganizationEntityAliase
            WHERE EMAOrganizationID =
                  @SandboxOrganizationID;

        IF OBJECT_ID(
            'dbo.DonorJourneyActivityType',
            'U'
        ) IS NOT NULL
            DELETE FROM dbo.DonorJourneyActivityType
            WHERE EMAOrganizationID =
                  @SandboxOrganizationID;

        IF OBJECT_ID(
            'dbo.DonorStatus',
            'U'
        ) IS NOT NULL
            DELETE FROM dbo.DonorStatus
            WHERE EMAOrganizationID =
                  @SandboxOrganizationID;

        DELETE FROM dbo.OrganizationEvent
        WHERE EMAOrganizationID =
              @SandboxOrganizationID;

        DELETE FROM dbo.OrganizationDonationSource
        WHERE EMAOrganizationID =
              @SandboxOrganizationID;

        DELETE FROM dbo.OrganizationTributeType
        WHERE EMAOrganizationID =
              @SandboxOrganizationID;

        DELETE FROM dbo.OrganizationFund
        WHERE EMAOrganizationID =
              @SandboxOrganizationID;

        DELETE FROM dbo.OrganizationCampus
        WHERE EMAOrganizationID =
              @SandboxOrganizationID;

        DELETE FROM dbo.OrganizationMethodOfGiving
        WHERE EMAOrganizationID =
              @SandboxOrganizationID;

        IF OBJECT_ID(
            'tempdb..#SandboxOrganizationUsers'
        ) IS NOT NULL
            DROP TABLE #SandboxOrganizationUsers;

        CREATE TABLE #SandboxOrganizationUsers
        (
            OrganizationUserID INT NOT NULL
                PRIMARY KEY
        );

        INSERT INTO #SandboxOrganizationUsers
        (
            OrganizationUserID
        )
        SELECT
            ou.OrganizationUserID
        FROM dbo.OrganizationUser ou
        WHERE ou.EMAOrganizationID =
              @SandboxOrganizationID;

        IF OBJECT_ID(
            'tempdb..#SandboxLoginInstances'
        ) IS NOT NULL
            DROP TABLE #SandboxLoginInstances;

        CREATE TABLE #SandboxLoginInstances
        (
            LoginInstanceID BIGINT NOT NULL
                PRIMARY KEY
        );

        IF OBJECT_ID(
            'dbo.LoginInstance',
            'U'
        ) IS NOT NULL
        BEGIN
            INSERT INTO #SandboxLoginInstances
            (
                LoginInstanceID
            )
            SELECT
                loginInstance.LoginInstanceID

            FROM dbo.LoginInstance loginInstance

            INNER JOIN #SandboxOrganizationUsers organizationUser
                ON organizationUser.OrganizationUserID =
                   loginInstance.OrganizationUserID;
        END;

        IF OBJECT_ID(
            'dbo.LoginInstanceEvent',
            'U'
        ) IS NOT NULL
            DELETE event
            FROM dbo.LoginInstanceEvent event

            INNER JOIN #SandboxLoginInstances loginInstance
                ON loginInstance.LoginInstanceID =
                   event.LoginInstanceID;

        IF OBJECT_ID(
            'dbo.MenuItemUsage',
            'U'
        ) IS NOT NULL
            DELETE usage
            FROM dbo.MenuItemUsage usage

            INNER JOIN #SandboxLoginInstances loginInstance
                ON loginInstance.LoginInstanceID =
                   usage.LoginInstanceID;

        IF OBJECT_ID(
            'dbo.LoginInstance',
            'U'
        ) IS NOT NULL
            DELETE loginInstance
            FROM dbo.LoginInstance loginInstance

            INNER JOIN #SandboxLoginInstances sandboxLogin
                ON sandboxLogin.LoginInstanceID =
                   loginInstance.LoginInstanceID;

        IF OBJECT_ID(
            'dbo.ApiSession',
            'U'
        ) IS NOT NULL
            DELETE FROM dbo.ApiSession
            WHERE EMAOrganizationID =
                  @SandboxOrganizationID;

        IF OBJECT_ID(
            'dbo.SandboxCapability',
            'U'
        ) IS NOT NULL
            DELETE FROM dbo.SandboxCapability
            WHERE SandboxOrganizationID =
                  @SandboxOrganizationID;

        DELETE FROM dbo.OrganizationUser
        WHERE EMAOrganizationID =
              @SandboxOrganizationID;

        DELETE userRole
        FROM dbo.OrganizationUserRole userRole

        INNER JOIN #SandboxUsers sandboxUser
            ON sandboxUser.UserID =
               userRole.UserID;

        DELETE sandboxUser
        FROM dbo.[User] sandboxUser

        INNER JOIN #SandboxUsers selectedUser
            ON selectedUser.UserID =
               sandboxUser.UserID

        WHERE NOT EXISTS
        (
            SELECT 1
            FROM dbo.OrganizationUser remainingLink
            WHERE remainingLink.UserID =
                  sandboxUser.UserID
        );

        DELETE FROM dbo.Organization
        WHERE EMAOrganizationID =
              @SandboxOrganizationID;

        COMMIT TRANSACTION;
    END TRY

    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;

        THROW;
    END CATCH;
END;
GO

/* -------------------------------------------------------------------------
   Monthly cleanup procedure. The Azure Timer function calls this on the first
   day of every month at midnight UTC.
   ------------------------------------------------------------------------- */

CREATE OR ALTER PROCEDURE dbo.sp_SandboxDeleteExpired
    @InactiveDays INT = 180
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @InactiveDays IS NULL
       OR @InactiveDays <= 0
        SET @InactiveDays = 180;

    CREATE TABLE #ExpiredSandboxes
    (
        SandboxOrganizationID INT NOT NULL
            PRIMARY KEY,

        SandboxUserID INT NULL,

        SandboxEmailAddress NVARCHAR(512) NULL
    );

    INSERT INTO #ExpiredSandboxes
    (
        SandboxOrganizationID,
        SandboxUserID,
        SandboxEmailAddress
    )
    SELECT
        o.EMAOrganizationID,
        sandboxUser.SandboxUserID,
        o.SandboxEtransferEmail

    FROM dbo.Organization o

    OUTER APPLY
    (
        SELECT
            MAX(d.DateTimeReceived) AS LastDonationDate
        FROM dbo.Donation d
        WHERE d.EMAOrganizationID =
              o.EMAOrganizationID
    ) activity

    OUTER APPLY
    (
        SELECT
            MIN(u.UserID) AS SandboxUserID,
            MIN(u.DateTimeCreated) AS SandboxUserCreatedDate
        FROM dbo.[User] u
        WHERE u.SandboxOrganizationID =
              o.EMAOrganizationID
    ) sandboxUser

    WHERE ISNULL(
        o.OrganizationStatusID,
        -1
    ) = 0
      AND COALESCE(
            activity.LastDonationDate,
            o.DateTimeCreated,
            sandboxUser.SandboxUserCreatedDate,
            '19000101'
          ) < DATEADD(
                DAY,
                -@InactiveDays,
                SYSUTCDATETIME()
          );

    DECLARE @SandboxOrganizationID INT;

    DECLARE cleanup_cursor
        CURSOR LOCAL FAST_FORWARD FOR

        SELECT SandboxOrganizationID
        FROM #ExpiredSandboxes
        ORDER BY SandboxOrganizationID;

    OPEN cleanup_cursor;

    FETCH NEXT FROM cleanup_cursor
        INTO @SandboxOrganizationID;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        EXEC dbo.sp_SandboxDeleteOne
            @SandboxOrganizationID =
                @SandboxOrganizationID;

        FETCH NEXT FROM cleanup_cursor
            INTO @SandboxOrganizationID;
    END;

    CLOSE cleanup_cursor;
    DEALLOCATE cleanup_cursor;

    SELECT
        SandboxOrganizationID,
        SandboxUserID,
        SandboxEmailAddress
    FROM #ExpiredSandboxes
    ORDER BY
        SandboxOrganizationID;
END;
GO
