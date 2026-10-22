-- ============================================================================
-- Rewritten sp_UpdateOrgUseFlags for the current Organization use-flag columns.
-- Run against emasqldatabase-dev via the mssql extension.
--
-- Replaces the stale proc, which referenced dropped columns
-- (UseCurrencyTypes / UseTributes / UseAliases) and therefore failed at runtime.
--
-- Current Organization flag columns (all CHAR(1) 'Y'/'N'):
--   UseCampuses, UseEvents, UseFunds, UseMethodsOfGiving, UseSources, UseTributeTypes, UseAnonymous
-- ============================================================================

CREATE OR ALTER PROCEDURE sp_UpdateOrgUseFlags
    @EMAOrganizationID  INT,
    @UseCampuses        CHAR(1),
    @UseEvents          CHAR(1),
    @UseFunds           CHAR(1),
    @UseMethodsOfGiving CHAR(1),
    @UseSources         CHAR(1),
    @UseTributeTypes    CHAR(1),
    @UseAnonymous       CHAR(1),
    @UpdatedByUserID    INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE Organization
    SET UseCampuses        = @UseCampuses,
        UseEvents          = @UseEvents,
        UseFunds           = @UseFunds,
        UseMethodsOfGiving = @UseMethodsOfGiving,
        UseSources         = @UseSources,
        UseTributeTypes    = @UseTributeTypes,
        UseAnonymous       = @UseAnonymous,
        DateTimeUpdated    = GETUTCDATE(),
        UpdatedByUserID    = @UpdatedByUserID
    WHERE EMAOrganizationID = @EMAOrganizationID;
END
GO
