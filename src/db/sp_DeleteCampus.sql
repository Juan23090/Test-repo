CREATE OR ALTER PROCEDURE dbo.sp_DeleteCampus
    @EMACampusID INT,
    @EMAOrganizationID INT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.OrganizationCampus
    WHERE EMACampusID = @EMACampusID
      AND EMAOrganizationID = @EMAOrganizationID;
END;
GO
