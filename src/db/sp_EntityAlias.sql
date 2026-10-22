-- ============================================================================
-- Entity Alias stored procedure.
-- Backed by OrganizationEntityAliase (live table name, trailing "e" typo preserved
-- intentionally) joined against ChMSEntityAlias for the canonical entity-name list.
-- Key-value override, not a flat list: no surrogate ID, no IsActive. Natural key is
-- (EMAOrganizationID, EntityName). CreatedByUsedID is a live typo'd column name -
-- preserved as-is, do not "fix" it.
-- ============================================================================

CREATE OR ALTER PROCEDURE sp_UpsertEntityAlias
    @EMAOrganizationID INT,
    @EntityName        VARCHAR(255),
    @AliasName         VARCHAR(255),
    @UpdatedByUserID   INT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (
        SELECT 1 FROM OrganizationEntityAliase
        WHERE EMAOrganizationID = @EMAOrganizationID AND EntityName = @EntityName)
    BEGIN
        UPDATE OrganizationEntityAliase
        SET AliasName       = @AliasName,
            DateTimeUpdated = GETUTCDATE(),
            UpdatedByUserID = @UpdatedByUserID
        WHERE EMAOrganizationID = @EMAOrganizationID AND EntityName = @EntityName;
    END
    ELSE
    BEGIN
        INSERT INTO OrganizationEntityAliase
            (EMAOrganizationID, EntityName, AliasName, DateTimeCreated, CreatedByUsedID)
        VALUES
            (@EMAOrganizationID, @EntityName, @AliasName, GETUTCDATE(), @UpdatedByUserID);
    END
END
GO
