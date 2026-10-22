-- Per-user fund shortlist: which of an organization's funds a given user sees in the
-- Donations screen's Fund dropdown, and in what order.
--
-- The table already exists on dev (created outside the repo); this script versions it so a
-- fresh environment can be built from source. Guarded so it is a no-op where it exists.
--
-- Selected defaults to 'Y', but sp_SyncUserFundShortList deliberately inserts 'N' so newly
-- matched funds arrive unchecked (per the task spec).
--
-- Note: OrganizationFund's primary key is EMAFundID alone -- fund IDs are unique across all
-- organizations -- so (UserID, EMAFundID) is a sufficient key and EMAOrganizationID is carried
-- for filtering.

IF NOT EXISTS
(
    SELECT 1
    FROM sys.tables
    WHERE object_id = OBJECT_ID('dbo.OrganizationUserFundShortList')
)
BEGIN
    CREATE TABLE dbo.OrganizationUserFundShortList
    (
        EMAOrganizationID INT      NOT NULL,
        UserID            INT      NOT NULL,
        EMAFundID         INT      NOT NULL,
        Sequence          INT      NULL,
        Selected          CHAR(1)  NULL CONSTRAINT DF_OrganizationUserFundShortList_Selected DEFAULT ('Y'),

        CONSTRAINT PK_OrganizationUserFundShortList
            PRIMARY KEY CLUSTERED (UserID, EMAFundID)
    );
END;
GO
