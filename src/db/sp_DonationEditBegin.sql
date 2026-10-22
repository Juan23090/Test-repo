CREATE OR ALTER PROCEDURE dbo.sp_DonationEditBegin
    @EMAOrganizationID INT,
    @EMADonationID BIGINT,
    @JsonPayload NVARCHAR(MAX),
    @UpdatedByUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF ISJSON(@JsonPayload) <> 1
    BEGIN
        THROW 52021, 'The donation edit payload is not valid JSON.', 1;
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
            THROW 52022, 'The donation was not found in the organization.', 1;
        END;

        INSERT INTO dbo.DonationAudit
        (
            EMADonationId,
            DateTimeCreated,
            UpdatedByUserID,
            DDDDJsonPayload
        )
        VALUES
        (
            @EMADonationID,
            SYSUTCDATETIME(),
            @UpdatedByUserID,
            @JsonPayload
        );

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
