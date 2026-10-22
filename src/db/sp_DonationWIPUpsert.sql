CREATE OR ALTER PROCEDURE dbo.sp_DonationWIPUpsert
    @EMAOrganizationID INT,
    @EMADonationID BIGINT,
    @JsonPayload NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF ISJSON(@JsonPayload) <> 1
    BEGIN
        THROW 52121, 'The donation WIP payload is not valid JSON.', 1;
    END;

    BEGIN TRANSACTION;
    BEGIN TRY
        IF NOT EXISTS
        (
            SELECT 1
            FROM dbo.Donation WITH (UPDLOCK, HOLDLOCK)
            WHERE EMADonationID = @EMADonationID
              AND EMAOrganizationID = @EMAOrganizationID
        )
        BEGIN
            THROW 52122, 'The donation was not found in the organization.', 1;
        END;

        UPDATE dbo.DonationWIP
        SET
            DDDDJasonPayload = @JsonPayload,
            DateTImeCreated = SYSUTCDATETIME()
        WHERE EMADonationID = @EMADonationID;

        IF @@ROWCOUNT = 0
        BEGIN
            INSERT INTO dbo.DonationWIP
            (
                EMADonationID,
                DDDDJasonPayload,
                DateTImeCreated
            )
            VALUES
            (
                @EMADonationID,
                @JsonPayload,
                SYSUTCDATETIME()
            );
        END;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0
        BEGIN
            ROLLBACK TRANSACTION;
        END;

        THROW;
    END CATCH;
END;
GO
