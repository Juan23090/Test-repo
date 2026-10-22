-- ============================================================================
-- Deposit Slip Revisions V2 (Roger, task 21): add Organization.CountingSteps.
-- Run against emasqldatabase-dev via the mssql extension (and other DBs when promoted).
--
-- The deposit slip (/deposit-slip) renders the counting steps as an editable, autosaved
-- text box backed by this column. GetOrganization now SELECTs it and UpdateOrganization
-- writes it, so this column must exist or those endpoints 500.
--
-- The other deposit-slip config fields (BankBranchNumber, BankAccountNumber,
-- EMAMethodOfGivingIDForCheques, EMAMethodOfGivingIDForCash) already exist on Organization
-- and only needed wiring in code — no schema change for them.
--
-- Idempotent: guarded so it is safe to re-run.
-- ============================================================================

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.Organization') AND name = 'CountingSteps'
)
BEGIN
    ALTER TABLE dbo.Organization ADD CountingSteps NVARCHAR(MAX) NULL;
END;
GO
