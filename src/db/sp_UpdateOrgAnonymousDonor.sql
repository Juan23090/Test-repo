-- ============================================================================
-- sp_UpdateOrgAnonymousDonor — sets the donor used for anonymous donations.
-- Run against emasqldatabase-dev via the mssql extension.
--
-- Updates Organization.EMADonorIDForAnonymous for one org.
-- ============================================================================

CREATE OR ALTER PROCEDURE sp_UpdateOrgAnonymousDonor
    @EMAOrganizationID      INT,
    @EMADonorIDForAnonymous INT,
    @UpdatedByUserID        INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.Donor
        WHERE EMADonorID =
              @EMADonorIDForAnonymous
          AND EMAOrganizationID =
              @EMAOrganizationID
    )
        THROW 51060,
            'The anonymous donor must belong to the organization.',
            1;

    UPDATE dbo.Organization
    SET EMADonorIDForAnonymous = @EMADonorIDForAnonymous,
        DateTimeUpdated        = GETUTCDATE(),
        UpdatedByUserID        = @UpdatedByUserID
    WHERE EMAOrganizationID = @EMAOrganizationID;
END
GO
