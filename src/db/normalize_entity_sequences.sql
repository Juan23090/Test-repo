-- ============================================================================
-- Normalize Sequence to distinct 1..N per org for every reorderable entity.
-- Run against emasqldatabase-dev (and prod when promoted).
--
-- Why: legacy rows had duplicate Sequence values (e.g. three org-1 campuses all
-- at Sequence 3) and some NULLs. The ▲/▼ reorder swaps two rows' Sequence values,
-- which is a no-op when they collide, so reordering appeared broken. This renumbers
-- each org's rows to distinct 1..N, fixing duplicates and NULLs in one pass, and
-- supersedes the earlier NULL-only backfills.
--
-- Order preserved: current Sequence then PK; NULL sequences sort last.
-- Events excluded — OrganizationEvent.Sequence is an IDENTITY column (not reorderable).
-- Idempotent: safe to re-run (already-normalized data stays put).
-- ============================================================================

WITH o AS (SELECT EMACampusID AS id, ROW_NUMBER() OVER (PARTITION BY EMAOrganizationID
    ORDER BY CASE WHEN Sequence IS NULL THEN 1 ELSE 0 END, Sequence, EMACampusID) rn
    FROM dbo.OrganizationCampus)
UPDATE c SET Sequence = o.rn FROM dbo.OrganizationCampus c JOIN o ON o.id = c.EMACampusID;

WITH o AS (SELECT EMAFundID AS id, ROW_NUMBER() OVER (PARTITION BY EMAOrganizationID
    ORDER BY CASE WHEN Sequence IS NULL THEN 1 ELSE 0 END, Sequence, EMAFundID) rn
    FROM dbo.OrganizationFund)
UPDATE c SET Sequence = o.rn FROM dbo.OrganizationFund c JOIN o ON o.id = c.EMAFundID;

WITH o AS (SELECT EMAMethodOfGivingID AS id, ROW_NUMBER() OVER (PARTITION BY EMAOrganizationID
    ORDER BY CASE WHEN Sequence IS NULL THEN 1 ELSE 0 END, Sequence, EMAMethodOfGivingID) rn
    FROM dbo.OrganizationMethodOfGiving)
UPDATE c SET Sequence = o.rn FROM dbo.OrganizationMethodOfGiving c JOIN o ON o.id = c.EMAMethodOfGivingID;

WITH o AS (SELECT EMADonationSourceID AS id, ROW_NUMBER() OVER (PARTITION BY EMAOrganizationID
    ORDER BY CASE WHEN Sequence IS NULL THEN 1 ELSE 0 END, Sequence, EMADonationSourceID) rn
    FROM dbo.OrganizationDonationSource)
UPDATE c SET Sequence = o.rn FROM dbo.OrganizationDonationSource c JOIN o ON o.id = c.EMADonationSourceID;

WITH o AS (SELECT EMATributeTypeID AS id, ROW_NUMBER() OVER (PARTITION BY EMAOrganizationID
    ORDER BY CASE WHEN Sequence IS NULL THEN 1 ELSE 0 END, Sequence, EMATributeTypeID) rn
    FROM dbo.OrganizationTributeType)
UPDATE c SET Sequence = o.rn FROM dbo.OrganizationTributeType c JOIN o ON o.id = c.EMATributeTypeID;

WITH o AS (SELECT EMAInteractionTypeID AS id, ROW_NUMBER() OVER (PARTITION BY EMAOrganizationID
    ORDER BY CASE WHEN Sequence IS NULL THEN 1 ELSE 0 END, Sequence, EMAInteractionTypeID) rn
    FROM dbo.OrganizationInteractionType)
UPDATE c SET Sequence = o.rn FROM dbo.OrganizationInteractionType c JOIN o ON o.id = c.EMAInteractionTypeID;

-- DonorStatus uses EMAOrganizationId (lowercase d) — the real column name on this table.
WITH o AS (SELECT DonorStatusID AS id, ROW_NUMBER() OVER (PARTITION BY EMAOrganizationId
    ORDER BY CASE WHEN Sequence IS NULL THEN 1 ELSE 0 END, Sequence, DonorStatusID) rn
    FROM dbo.DonorStatus)
UPDATE c SET Sequence = o.rn FROM dbo.DonorStatus c JOIN o ON o.id = c.DonorStatusID;
