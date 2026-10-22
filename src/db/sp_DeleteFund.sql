CREATE OR ALTER PROCEDURE dbo.sp_DeleteFund
    @EMAFundID INT,
    @EMAOrganizationID INT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.OrganizationFund
    WHERE EMAFundID = @EMAFundID
      AND EMAOrganizationID = @EMAOrganizationID;
END;
GO
