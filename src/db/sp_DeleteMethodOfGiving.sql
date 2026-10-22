CREATE OR ALTER PROCEDURE dbo.sp_DeleteMethodOfGiving
    @EMAMethodOfGivingID INT,
    @EMAOrganizationID INT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.OrganizationMethodOfGiving
    WHERE EMAMethodOfGivingID = @EMAMethodOfGivingID
      AND EMAOrganizationId = @EMAOrganizationID;
END;
GO
