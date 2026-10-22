/*
    Removes donor preference columns that duplicate the existing canonical
    donor settings.

    Canonical columns:
      - IsDonorPilotingEtransfers
      - AcknowledgmentForManualTransactions
      - AcknowledgementForAutomatedTransactions

    No values are copied from the duplicate acknowledgement column because
    one coarse value cannot safely replace two independent canonical choices.

    Idempotent - safe to run more than once.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF COL_LENGTH('dbo.Donor', 'IsDonorPilotingEtransfers') IS NULL
    THROW 53001, 'dbo.Donor.IsDonorPilotingEtransfers is required.', 1;
IF COL_LENGTH('dbo.Donor', 'AcknowledgmentForManualTransactions') IS NULL
    THROW 53002, 'dbo.Donor.AcknowledgmentForManualTransactions is required.', 1;
IF COL_LENGTH('dbo.Donor', 'AcknowledgementForAutomatedTransactions') IS NULL
    THROW 53003, 'dbo.Donor.AcknowledgementForAutomatedTransactions is required.', 1;

IF OBJECT_DEFINITION(OBJECT_ID('dbo.sp_CreateDonor')) IS NULL
   OR OBJECT_DEFINITION(OBJECT_ID('dbo.sp_CreateDonor'))
        LIKE '%PilotEtransferIngestion%'
   OR OBJECT_DEFINITION(OBJECT_ID('dbo.sp_CreateDonor'))
        LIKE '%ReceiveAcknowledgementsThatArentEtransfers%'
    THROW 53004,
        'Apply the canonical dbo.sp_CreateDonor definition before removing duplicate donor columns.',
        1;

IF OBJECT_DEFINITION(OBJECT_ID('dbo.sp_UpdateDonor')) IS NULL
   OR OBJECT_DEFINITION(OBJECT_ID('dbo.sp_UpdateDonor'))
        LIKE '%PilotEtransferIngestion%'
   OR OBJECT_DEFINITION(OBJECT_ID('dbo.sp_UpdateDonor'))
        LIKE '%ReceiveAcknowledgementsThatArentEtransfers%'
    THROW 53005,
        'Apply the canonical dbo.sp_UpdateDonor definition before removing duplicate donor columns.',
        1;

BEGIN TRANSACTION;
BEGIN TRY
    DECLARE @ConstraintName SYSNAME;
    DECLARE @Sql NVARCHAR(MAX);

    IF EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE parent_object_id = OBJECT_ID('dbo.Donor')
                 AND name = 'CK_Donor_PilotEtransferIngestion')
        ALTER TABLE dbo.Donor DROP CONSTRAINT CK_Donor_PilotEtransferIngestion;

    SELECT @ConstraintName = dc.name
    FROM sys.default_constraints dc
    INNER JOIN sys.columns c
        ON c.object_id = dc.parent_object_id
       AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID('dbo.Donor')
      AND c.name = 'PilotEtransferIngestion';

    IF @ConstraintName IS NOT NULL
    BEGIN
        SET @Sql = N'ALTER TABLE dbo.Donor DROP CONSTRAINT '
            + QUOTENAME(@ConstraintName) + N';';
        EXEC sys.sp_executesql @Sql;
    END;

    IF COL_LENGTH('dbo.Donor', 'PilotEtransferIngestion') IS NOT NULL
        ALTER TABLE dbo.Donor DROP COLUMN PilotEtransferIngestion;

    SET @ConstraintName = NULL;

    IF EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE parent_object_id = OBJECT_ID('dbo.Donor')
                 AND name = 'CK_Donor_ReceiveAcknowledgementsThatArentEtransfers')
        ALTER TABLE dbo.Donor
            DROP CONSTRAINT CK_Donor_ReceiveAcknowledgementsThatArentEtransfers;

    SELECT @ConstraintName = dc.name
    FROM sys.default_constraints dc
    INNER JOIN sys.columns c
        ON c.object_id = dc.parent_object_id
       AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID('dbo.Donor')
      AND c.name = 'ReceiveAcknowledgementsThatArentEtransfers';

    IF @ConstraintName IS NOT NULL
    BEGIN
        SET @Sql = N'ALTER TABLE dbo.Donor DROP CONSTRAINT '
            + QUOTENAME(@ConstraintName) + N';';
        EXEC sys.sp_executesql @Sql;
    END;

    IF COL_LENGTH('dbo.Donor', 'ReceiveAcknowledgementsThatArentEtransfers') IS NOT NULL
        ALTER TABLE dbo.Donor
            DROP COLUMN ReceiveAcknowledgementsThatArentEtransfers;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

SELECT c.name AS CanonicalColumn
FROM sys.columns c
WHERE c.object_id = OBJECT_ID('dbo.Donor')
  AND c.name IN
  (
      'IsDonorPilotingEtransfers',
      'AcknowledgmentForManualTransactions',
      'AcknowledgementForAutomatedTransactions'
  )
ORDER BY c.column_id;
GO
