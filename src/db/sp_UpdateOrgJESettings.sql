-- ============================================================================
-- Journal Entry screen settings update for the Organization.
-- Backed by dbo.Organization. Run against emasqldatabase-dev via the mssql extension.
--
-- Persists only the two JE-screen selections so the screen can save them without
-- touching the rest of the organization record:
--   JESummarizeBy CHAR(1)  -- 'A' = Account, 'D' = Description
--   JEByDate      CHAR(1)  -- 'Y' = group by date, 'N' = single journal
-- NOTE: the Organization audit column is "UpdatedByUserID" (capital B) — matches the table.
-- ============================================================================

CREATE OR ALTER PROCEDURE sp_UpdateOrgJESettings
    @EMAOrganizationID INT,
    @JESummarizeBy     CHAR(1),
    @JEByDate          CHAR(1),
    @UpdatedByUserID   INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE Organization
    SET JESummarizeBy   = @JESummarizeBy,
        JEByDate        = @JEByDate,
        DateTimeUpdated = GETUTCDATE(),
        UpdatedByUserID = @UpdatedByUserID
    WHERE EMAOrganizationID = @EMAOrganizationID;
END
GO
