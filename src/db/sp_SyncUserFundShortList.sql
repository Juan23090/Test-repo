-- Reconciles a user's fund shortlist against the organization's current funds, then returns it.
-- Runs every time the shortlist overlay opens, which is what keeps the list correct as funds
-- are added and retired.
--
-- Per the task spec:
--   1. Match all ACTIVE funds into the shortlist for this Org & User, inserting them UNCHECKED.
--   2. Then remove every row whose fund is inactive (or gone), regardless of the checkmark.
--
-- Existing rows are never updated, so checkmarks the user already set are preserved.

CREATE OR ALTER PROCEDURE dbo.sp_SyncUserFundShortList
    @EMAOrganizationID INT,
    @UserID            INT
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRANSACTION;

    -- 1. Add any active org fund this user has no row for, unchecked.
    --    Seeded with the fund's own Sequence so a first-time list matches the Entities order.
    INSERT INTO dbo.OrganizationUserFundShortList
        (EMAOrganizationID, UserID, EMAFundID, Sequence, Selected)
    SELECT
        f.EMAOrganizationID,
        @UserID,
        f.EMAFundID,
        f.Sequence,
        'N'
    FROM dbo.OrganizationFund AS f
    WHERE f.EMAOrganizationID = @EMAOrganizationID
      AND f.IsActive = 'Y'
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.OrganizationUserFundShortList AS s
          WHERE s.UserID = @UserID
            AND s.EMAFundID = f.EMAFundID
      );

    -- 2. Drop rows whose fund is no longer active or no longer exists, checked or not.
    DELETE s
    FROM dbo.OrganizationUserFundShortList AS s
    LEFT JOIN dbo.OrganizationFund AS f
        ON f.EMAFundID = s.EMAFundID
    WHERE s.UserID = @UserID
      AND s.EMAOrganizationID = @EMAOrganizationID
      AND (f.EMAFundID IS NULL OR f.IsActive <> 'Y');

    COMMIT TRANSACTION;

    -- 3. Return the reconciled list. EMAFundID breaks ties so the order is deterministic
    --    even if sequences are duplicated or NULL.
    SELECT
        s.EMAFundID,
        f.Name AS FundName,
        s.Sequence,
        s.Selected
    FROM dbo.OrganizationUserFundShortList AS s
    INNER JOIN dbo.OrganizationFund AS f
        ON f.EMAFundID = s.EMAFundID
    WHERE s.UserID = @UserID
      AND s.EMAOrganizationID = @EMAOrganizationID
    ORDER BY s.Sequence, s.EMAFundID;
END;
GO
