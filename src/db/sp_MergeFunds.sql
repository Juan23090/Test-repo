CREATE OR ALTER PROCEDURE dbo.sp_MergeFunds
    @FromID INT,
    @ToID INT,
    @EMAOrganizationID INT,
    @UpdatedByUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @FromOrgID INT, @ToOrgID INT;
    SELECT @FromOrgID = EMAOrganizationID FROM dbo.OrganizationFund WHERE EMAFundID = @FromID;
    SELECT @ToOrgID = EMAOrganizationID FROM dbo.OrganizationFund WHERE EMAFundID = @ToID;

    IF @FromOrgID IS NULL
       OR @ToOrgID IS NULL
       OR @FromOrgID <> @ToOrgID
       OR @FromOrgID <> @EMAOrganizationID
    BEGIN
        RAISERROR ('Both funds must exist and belong to the same organization.', 16, 1);
        RETURN;
    END

    BEGIN TRANSACTION;
    BEGIN TRY
        UPDATE to_fund
        SET 
            ChMSFundID = COALESCE(NULLIF(to_fund.ChMSFundID, ''), NULLIF(from_fund.ChMSFundID, '')),
            GLAccountNumber = COALESCE(NULLIF(to_fund.GLAccountNumber, ''), NULLIF(from_fund.GLAccountNumber, '')),
            TaxDeductible = COALESCE(to_fund.TaxDeductible, from_fund.TaxDeductible),
            EMACampusID = COALESCE(to_fund.EMACampusID, from_fund.EMACampusID),
            DateTimeUpdated = GETUTCDATE(),
            UpdatedbyUserID = @UpdatedByUserID
        FROM dbo.OrganizationFund to_fund
        INNER JOIN dbo.OrganizationFund from_fund
            ON from_fund.EMAFundID = @FromID
           AND from_fund.EMAOrganizationID =
               @EMAOrganizationID
        WHERE to_fund.EMAFundID = @ToID
          AND to_fund.EMAOrganizationID =
              @EMAOrganizationID;

        UPDATE distribution
        SET EMAFundID = @ToID
        FROM dbo.DonationDistribution distribution
        INNER JOIN dbo.Donation donation
            ON donation.EMADonationID =
               distribution.EMADonationID
        WHERE distribution.EMAFundID = @FromID
          AND donation.EMAOrganizationID =
              @EMAOrganizationID;

        UPDATE dbo.Organization
        SET EMAFundIDForEtransfer = CASE WHEN EMAFundIDForEtransfer = @FromID THEN @ToID ELSE EMAFundIDForEtransfer END,
            EMAFundIDForPADs = CASE WHEN EMAFundIDForPADs = @FromID THEN @ToID ELSE EMAFundIDForPADs END,
            DateTimeUpdated = GETUTCDATE(),
            UpdatedByUserID = @UpdatedByUserID
        WHERE (EMAFundIDForEtransfer = @FromID OR EMAFundIDForPADs = @FromID)
          AND EMAOrganizationID =
              @EMAOrganizationID;

        UPDATE dbo.OrganizationCampus
        SET DefaultCampusFundID = @ToID,
            DateTimeUpdated = GETUTCDATE(),
            UpdatedbyUserID = @UpdatedByUserID
        WHERE DefaultCampusFundID = @FromID
          AND EMAOrganizationID =
              @EMAOrganizationID;

        DELETE FROM dbo.OrganizationFund
        WHERE EMAFundID = @FromID
          AND EMAOrganizationID =
              @EMAOrganizationID;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO
