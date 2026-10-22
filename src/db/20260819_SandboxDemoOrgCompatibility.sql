/*
    Sandbox demo-template compatibility
    -----------------------------------
    Makes sandbox cloning safe when the template organization has the newer
    donation-channel default IDs configured.

    This migration is intentionally idempotent. It:
      - creates helpers that remap/clear organization-owned defaults
      - hooks the remap helper into dbo.sp_SandboxCreate
      - hooks the clear helper into dbo.sp_SandboxDeleteOne
      - adds an early sandbox guard to TR_Organization_AfterInsert when that
        trigger exists, preventing normal-organization onboarding side effects
        from running for OrganizationStatusID = 0

    Re-running this migration after sp_UserSandbox.sql is safe and will restore
    the hooks if the base sandbox procedures were reinstalled.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

CREATE OR ALTER PROCEDURE dbo.sp_SandboxApplyOrganizationDefaultMap
    @TemplateOrganizationID INT,
    @SandboxOrganizationID  INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF OBJECT_ID('tempdb..#SandboxCloneMap') IS NULL
        THROW 51060,
            '#SandboxCloneMap is required to remap sandbox organization defaults.',
            1;

    DECLARE @Mappings TABLE
    (
        ColumnName SYSNAME NOT NULL PRIMARY KEY,
        EntityName SYSNAME NOT NULL
    );

    INSERT INTO @Mappings (ColumnName, EntityName)
    VALUES
        (N'EMADonorIDForAnonymous',                 N'Donor'),
        (N'EMAEventIDForCreditDebitCards',          N'Event'),
        (N'EMASourceIDForCreditDebitCards',         N'DonationSource'),
        (N'EMAMethodOfGivingIDForCreditDebitCards', N'Method'),
        (N'EMAFundIDForCreditDebitCards',           N'Fund'),
        (N'EMACampusIDForCreditDebitCards',         N'Campus'),
        (N'EMAEventIDForEtransfers',                N'Event'),
        (N'EMASourceIDForEtransfers',               N'DonationSource'),
        (N'EMAMethodOfGivingIDForETransfers',       N'Method'),
        (N'EMAFundIDForEtransfer',                  N'Fund'),
        (N'EMACampusIDForEtransfers',               N'Campus'),
        (N'EMAEventIDForPADs',                      N'Event'),
        (N'EMASourceIDForPADs',                     N'DonationSource'),
        (N'EMAMethodOfGivingIDForPADs',             N'Method'),
        (N'EMAFundIDForPADs',                       N'Fund'),
        (N'EMACampusIDForPADs',                     N'Campus'),
        (N'EMAMethodOfGivingIDForCash',             N'Method'),
        (N'EMAMethodOfGivingIDForCheques',          N'Method');

    DECLARE
        @ColumnName SYSNAME,
        @EntityName SYSNAME,
        @Sql NVARCHAR(MAX);

    DECLARE mapping_cursor CURSOR LOCAL FAST_FORWARD FOR
        SELECT ColumnName, EntityName
        FROM @Mappings
        ORDER BY ColumnName;

    OPEN mapping_cursor;

    FETCH NEXT FROM mapping_cursor
        INTO @ColumnName, @EntityName;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        IF COL_LENGTH('dbo.Organization', @ColumnName) IS NOT NULL
        BEGIN
            SET @Sql =
                N'UPDATE targetOrganization
                  SET ' + QUOTENAME(@ColumnName) + N' =
                      CASE
                          WHEN sourceOrganization.' + QUOTENAME(@ColumnName) + N' IS NULL
                              THEN NULL
                          ELSE
                              (
                                  SELECT TOP (1)
                                      CONVERT(INT, map.NewID)
                                  FROM #SandboxCloneMap map
                                  WHERE map.EntityName = @EntityName
                                    AND map.OldID = CONVERT(
                                        BIGINT,
                                        sourceOrganization.' + QUOTENAME(@ColumnName) + N'
                                    )
                              )
                      END
                  FROM dbo.Organization targetOrganization
                  CROSS JOIN dbo.Organization sourceOrganization
                  WHERE targetOrganization.EMAOrganizationID = @SandboxOrganizationID
                    AND sourceOrganization.EMAOrganizationID = @TemplateOrganizationID;';

            EXEC sys.sp_executesql
                @Sql,
                N'@EntityName SYSNAME,
                  @TemplateOrganizationID INT,
                  @SandboxOrganizationID INT',
                @EntityName = @EntityName,
                @TemplateOrganizationID = @TemplateOrganizationID,
                @SandboxOrganizationID = @SandboxOrganizationID;
        END;

        FETCH NEXT FROM mapping_cursor
            INTO @ColumnName, @EntityName;
    END;

    CLOSE mapping_cursor;
    DEALLOCATE mapping_cursor;
END;
GO

CREATE OR ALTER PROCEDURE dbo.sp_SandboxClearOrganizationDefaults
    @SandboxOrganizationID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Columns TABLE
    (
        ColumnName SYSNAME NOT NULL PRIMARY KEY
    );

    INSERT INTO @Columns (ColumnName)
    VALUES
        (N'EMADonorIDForAnonymous'),
        (N'EMAEventIDForCreditDebitCards'),
        (N'EMASourceIDForCreditDebitCards'),
        (N'EMAMethodOfGivingIDForCreditDebitCards'),
        (N'EMAFundIDForCreditDebitCards'),
        (N'EMACampusIDForCreditDebitCards'),
        (N'EMAEventIDForEtransfers'),
        (N'EMASourceIDForEtransfers'),
        (N'EMAMethodOfGivingIDForETransfers'),
        (N'EMAFundIDForEtransfer'),
        (N'EMACampusIDForEtransfers'),
        (N'EMAEventIDForPADs'),
        (N'EMASourceIDForPADs'),
        (N'EMAMethodOfGivingIDForPADs'),
        (N'EMAFundIDForPADs'),
        (N'EMACampusIDForPADs'),
        (N'EMAMethodOfGivingIDForCash'),
        (N'EMAMethodOfGivingIDForCheques');

    DECLARE @SetClause NVARCHAR(MAX);

    SELECT @SetClause =
        STRING_AGG(
            CONVERT(NVARCHAR(MAX), QUOTENAME(ColumnName) + N' = NULL'),
            N', '
        )
    FROM @Columns
    WHERE COL_LENGTH('dbo.Organization', ColumnName) IS NOT NULL;

    IF NULLIF(@SetClause, N'') IS NOT NULL
    BEGIN
        DECLARE @Sql NVARCHAR(MAX) =
            N'UPDATE dbo.Organization
              SET ' + @SetClause + N'
              WHERE EMAOrganizationID = @SandboxOrganizationID;';

        EXEC sys.sp_executesql
            @Sql,
            N'@SandboxOrganizationID INT',
            @SandboxOrganizationID = @SandboxOrganizationID;
    END;
END;
GO

/* -------------------------------------------------------------------------
   Hook the full default remap into the existing sandbox create procedure.
   The insertion point is immediately before the optional direct-table clones,
   after Campus/Fund/Method/Event/Source/Donor maps have been built.
   ------------------------------------------------------------------------- */

DECLARE @CreateDefinition NVARCHAR(MAX) =
    OBJECT_DEFINITION(OBJECT_ID('dbo.sp_SandboxCreate'));

IF @CreateDefinition IS NULL
    THROW 51061,
        'dbo.sp_SandboxCreate must exist before applying sandbox demo compatibility.',
        1;

IF CHARINDEX(
       N'sp_SandboxApplyOrganizationDefaultMap',
       @CreateDefinition
   ) = 0
BEGIN
    DECLARE @CreateMarker NVARCHAR(200) =
        N'EXEC dbo.sp_SandboxCloneDirectTable';

    DECLARE @CreateInsertPosition INT =
        CHARINDEX(@CreateMarker, @CreateDefinition);

    IF @CreateInsertPosition = 0
        THROW 51062,
            'Could not locate the sandbox-create remap insertion point.',
            1;

    SET @CreateDefinition =
        STUFF(
            @CreateDefinition,
            @CreateInsertPosition,
            0,
            N'EXEC dbo.sp_SandboxApplyOrganizationDefaultMap
            @TemplateOrganizationID = @TemplateOrganizationID,
            @SandboxOrganizationID = @NewOrganizationID;

        '
        );

    /*
        OBJECT_DEFINITION() can return CREATE PROCEDURE even when the source
        script used CREATE OR ALTER. Do not rely on exact header formatting.
        Rebuild the header from the PROCEDURE keyword forward so the dynamic
        batch is guaranteed to ALTER the existing object.
    */
    DECLARE @CreateProcedureToken INT =
        CHARINDEX(N'PROCEDURE', UPPER(@CreateDefinition));

    IF @CreateProcedureToken = 0
        THROW 51070,
            'Could not locate the PROCEDURE keyword in dbo.sp_SandboxCreate.',
            1;

    SET @CreateDefinition =
        N'ALTER PROCEDURE' +
        SUBSTRING(
            @CreateDefinition,
            @CreateProcedureToken + LEN(N'PROCEDURE'),
            LEN(@CreateDefinition)
        );

    EXEC sys.sp_executesql @CreateDefinition;
END;
GO

/* -------------------------------------------------------------------------
   Clear every organization-owned default before child entities are deleted.
   This prevents newer Fund/Campus/Method/Event/Source foreign keys from
   blocking sandbox cleanup.
   ------------------------------------------------------------------------- */

DECLARE @DeleteDefinition NVARCHAR(MAX) =
    OBJECT_DEFINITION(OBJECT_ID('dbo.sp_SandboxDeleteOne'));

IF @DeleteDefinition IS NULL
    THROW 51063,
        'dbo.sp_SandboxDeleteOne must exist before applying sandbox demo compatibility.',
        1;

IF CHARINDEX(
       N'sp_SandboxClearOrganizationDefaults',
       @DeleteDefinition
   ) = 0
BEGIN
    DECLARE @DeleteMarker NVARCHAR(200) =
        N'UPDATE dbo.Organization';

    DECLARE @DeleteInsertPosition INT =
        CHARINDEX(@DeleteMarker, @DeleteDefinition);

    IF @DeleteInsertPosition = 0
        THROW 51064,
            'Could not locate the sandbox-delete default-clear insertion point.',
            1;

    SET @DeleteDefinition =
        STUFF(
            @DeleteDefinition,
            @DeleteInsertPosition,
            0,
            N'EXEC dbo.sp_SandboxClearOrganizationDefaults
            @SandboxOrganizationID = @SandboxOrganizationID;

        '
        );

    DECLARE @DeleteProcedureToken INT =
        CHARINDEX(N'PROCEDURE', UPPER(@DeleteDefinition));

    IF @DeleteProcedureToken = 0
        THROW 51071,
            'Could not locate the PROCEDURE keyword in dbo.sp_SandboxDeleteOne.',
            1;

    SET @DeleteDefinition =
        N'ALTER PROCEDURE' +
        SUBSTRING(
            @DeleteDefinition,
            @DeleteProcedureToken + LEN(N'PROCEDURE'),
            LEN(@DeleteDefinition)
        );

    EXEC sys.sp_executesql @DeleteDefinition;
END;
GO

/* -------------------------------------------------------------------------
   Organization trigger guard.

   The onboarding trigger owns normal-organization user/role/journey creation.
   Sandbox organizations create their own purpose-built sandbox user and must
   not receive those normal onboarding side effects.

   The guard only returns early when every row in inserted is a sandbox row,
   so mixed/non-sandbox inserts retain the trigger's existing behavior.
   ------------------------------------------------------------------------- */

IF OBJECT_ID('dbo.TR_Organization_AfterInsert', 'TR') IS NOT NULL
BEGIN
    DECLARE @TriggerDefinition NVARCHAR(MAX) =
        OBJECT_DEFINITION(OBJECT_ID('dbo.TR_Organization_AfterInsert'));

    IF @TriggerDefinition IS NULL
        THROW 51065,
            'TR_Organization_AfterInsert exists but its definition could not be read.',
            1;

    DECLARE @SandboxGuard NVARCHAR(MAX) =
        N'
    -- EMA-SANDBOX-GUARD
    IF EXISTS
    (
        SELECT 1
        FROM inserted
        WHERE ISNULL(OrganizationStatusID, -1) = 0
    )
       AND NOT EXISTS
       (
           SELECT 1
           FROM inserted
           WHERE ISNULL(OrganizationStatusID, -1) <> 0
       )
        RETURN;
';

    /*
        Remove an existing guard before reinstalling it. This repairs databases
        where an earlier version placed the guard inside a nested BEGIN block.
    */
    DECLARE @ExistingGuardPosition INT =
        CHARINDEX(
            N'-- EMA-SANDBOX-GUARD',
            @TriggerDefinition
        );

    IF @ExistingGuardPosition > 0
    BEGIN
        DECLARE @ExistingGuardEndPosition INT =
            CHARINDEX(
                N'RETURN;',
                UPPER(@TriggerDefinition),
                @ExistingGuardPosition
            );

        IF @ExistingGuardEndPosition = 0
            THROW 51073,
                'The existing Organization sandbox guard is incomplete and could not be repaired.',
                1;

        SET @TriggerDefinition =
            STUFF(
                @TriggerDefinition,
                @ExistingGuardPosition,
                @ExistingGuardEndPosition
                    - @ExistingGuardPosition
                    + LEN(N'RETURN;'),
                N''
            );
    END;

    /*
        The trigger body is the first BEGIN after the TRIGGER keyword. Searching
        for " AS" is unsafe because AS may begin a line in the trigger header,
        causing the search to match a later table alias instead.
    */
    DECLARE @TriggerToken INT =
        CHARINDEX(N'TRIGGER', UPPER(@TriggerDefinition));

    IF @TriggerToken = 0
        THROW 51072,
            'Could not locate the TRIGGER keyword in TR_Organization_AfterInsert.',
            1;

    DECLARE @BeginPosition INT =
        CHARINDEX(
            N'BEGIN',
            UPPER(@TriggerDefinition),
            @TriggerToken + LEN(N'TRIGGER')
        );

    IF @BeginPosition = 0
        THROW 51066,
            'Could not locate the Organization insert trigger body.',
            1;

    SET @BeginPosition =
        @BeginPosition + LEN(N'BEGIN');

    SET @TriggerDefinition =
        STUFF(
            @TriggerDefinition,
            @BeginPosition,
            0,
            @SandboxGuard
        );

    SET @TriggerDefinition =
        N'ALTER TRIGGER' +
        SUBSTRING(
            @TriggerDefinition,
            @TriggerToken + LEN(N'TRIGGER'),
            LEN(@TriggerDefinition)
        );

    EXEC sys.sp_executesql @TriggerDefinition;
END;
GO

/* -------------------------------------------------------------------------
   Installation verification
   ------------------------------------------------------------------------- */

IF CHARINDEX(
       N'sp_SandboxApplyOrganizationDefaultMap',
       OBJECT_DEFINITION(OBJECT_ID('dbo.sp_SandboxCreate'))
   ) = 0
    THROW 51067,
        'Sandbox create compatibility hook was not installed.',
        1;

IF CHARINDEX(
       N'sp_SandboxClearOrganizationDefaults',
       OBJECT_DEFINITION(OBJECT_ID('dbo.sp_SandboxDeleteOne'))
   ) = 0
    THROW 51068,
        'Sandbox delete compatibility hook was not installed.',
        1;

IF OBJECT_ID('dbo.TR_Organization_AfterInsert', 'TR') IS NOT NULL
BEGIN
    DECLARE @InstalledTriggerDefinition NVARCHAR(MAX) =
        OBJECT_DEFINITION(OBJECT_ID('dbo.TR_Organization_AfterInsert'));

    DECLARE @InstalledTriggerToken INT =
        CHARINDEX(N'TRIGGER', UPPER(@InstalledTriggerDefinition));

    DECLARE @InstalledBeginPosition INT =
        CHARINDEX(
            N'BEGIN',
            UPPER(@InstalledTriggerDefinition),
            @InstalledTriggerToken + LEN(N'TRIGGER')
        );

    DECLARE @InstalledGuardPosition INT =
        CHARINDEX(
            N'EMA-SANDBOX-GUARD',
            @InstalledTriggerDefinition
        );

    DECLARE @InstalledNoCountPosition INT =
        CHARINDEX(
            N'SET NOCOUNT ON',
            UPPER(@InstalledTriggerDefinition),
            @InstalledBeginPosition
        );

    IF @InstalledTriggerToken = 0
       OR @InstalledBeginPosition = 0
       OR @InstalledGuardPosition <= @InstalledBeginPosition
       OR @InstalledNoCountPosition = 0
       OR @InstalledGuardPosition >= @InstalledNoCountPosition
        THROW 51069,
            'The Organization insert trigger sandbox guard was not installed at the start of the trigger body.',
            1;
END;
GO
