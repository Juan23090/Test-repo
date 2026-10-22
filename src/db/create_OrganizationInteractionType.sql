-- ============================================================================
-- OrganizationInteractionType table.
-- Run against emasqldatabase-dev via the mssql extension (and prod when promoted).
--
-- This table was scaffolded end-to-end in code (GetInteractionTypes / PostInteractionType /
-- PutInteractionType + sp_InsertInteractionType / sp_UpdateInteractionType + CopyDefaultEntities)
-- but the DDL "handed to Roger" (see the header in sp_InteractionType.sql) was never deployed,
-- so every Interaction Types path 500s. Columns below match exactly what those procs/endpoints
-- read and write:
--   * EMAInteractionTypeID  IDENTITY PK (procs return SCOPE_IDENTITY()).
--   * Sequence              nullable per-org ordering (insert assigns MAX+1; update swaps).
--   * IsActive              CHAR(1) 'Y'/'N', defaults 'Y'.
--   * Audit columns         written by the insert/update procs.
-- Idempotent: guarded so it is safe to re-run.
-- ============================================================================

IF OBJECT_ID('dbo.OrganizationInteractionType', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.OrganizationInteractionType
    (
        EMAInteractionTypeID INT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_OrganizationInteractionType PRIMARY KEY,
        EMAOrganizationID    INT           NOT NULL,
        Sequence             INT           NULL,
        Description          VARCHAR(255)  NOT NULL,
        IsActive             CHAR(1)       NOT NULL
            CONSTRAINT DF_OrganizationInteractionType_IsActive DEFAULT ('Y'),
        DateTimeCreated      DATETIME2     NULL,
        CreatedByUserID      INT           NULL,
        DateTimeUpdated      DATETIME2     NULL,
        UpdatedByUserID      INT           NULL
    );
END;
GO
