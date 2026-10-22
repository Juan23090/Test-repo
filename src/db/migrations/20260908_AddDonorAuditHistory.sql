SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'dbo.DonorAudit', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DonorAudit
    (
        DonorAuditID BIGINT IDENTITY(1, 1) NOT NULL
            CONSTRAINT PK_DonorAudit PRIMARY KEY,
        EMADonorID INT NOT NULL,
        EMAOrganizationID INT NOT NULL,
        DonorAuditPayload NVARCHAR(MAX) NOT NULL,
        ChangedByUserID INT NULL,
        DateTimeCreated DATETIME2(7) NOT NULL
            CONSTRAINT DF_DonorAudit_DateTimeCreated
            DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_DonorAudit_Donor
            FOREIGN KEY (EMADonorID)
            REFERENCES dbo.Donor (EMADonorID),
        CONSTRAINT FK_DonorAudit_Organization
            FOREIGN KEY (EMAOrganizationID)
            REFERENCES dbo.Organization (EMAOrganizationID),
        CONSTRAINT CK_DonorAudit_Payload_IsJson
            CHECK (ISJSON(DonorAuditPayload) = 1)
    );

    CREATE INDEX IX_DonorAudit_Organization_Donor_DateTime
        ON dbo.DonorAudit
        (
            EMAOrganizationID,
            EMADonorID,
            DateTimeCreated DESC,
            DonorAuditID DESC
        )
        INCLUDE
        (
            ChangedByUserID
        );
END;
GO

CREATE OR ALTER PROCEDURE dbo.sp_UpdateDonor
    @EMADonorID INT,
    @EMAOrganizationID INT,
    @EmailAddress VARCHAR(MAX),
    @InteracDonorName VARCHAR(MAX),
    @Name VARCHAR(MAX),
    @Address VARCHAR(MAX),
    @City VARCHAR(MAX),
    @ProvinceStateCode CHAR(2),
    @PostalCode VARCHAR(MAX),
    @Country VARCHAR(MAX),
    @PhoneNumber VARCHAR(MAX),
    @EMADefaultCampusID INT,
    @DonorStatusID INT,
    @IsDonorPilotingEtransfers CHAR(1) = NULL,
    @AcknowledgmentForManualTransactions CHAR(1) = NULL,
    @AcknowledgementForAutomatedTransactions CHAR(1) = NULL,
    @UpdatedByUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @EMADefaultCampusID IS NOT NULL
       AND NOT EXISTS
       (
           SELECT 1
           FROM dbo.OrganizationCampus
           WHERE EMAOrganizationID = @EMAOrganizationID
             AND EMACampusID = @EMADefaultCampusID
       )
    BEGIN
        THROW 52011, 'The default campus does not belong to the organization.', 1;
    END;

    IF @DonorStatusID IS NOT NULL
       AND NOT EXISTS
       (
           SELECT 1
           FROM dbo.DonorStatus
           WHERE EMAOrganizationId = @EMAOrganizationID
             AND DonorStatusID = @DonorStatusID
       )
    BEGIN
        THROW 52012, 'The donor status does not belong to the organization.', 1;
    END;

    IF NULLIF(LTRIM(RTRIM(@Name)), '') IS NULL
       AND NULLIF(LTRIM(RTRIM(@InteracDonorName)), '') IS NULL
    BEGIN
        THROW 52013, 'At least one donor name is required.', 1;
    END;

    BEGIN TRANSACTION;

    BEGIN TRY
        INSERT INTO dbo.DonorAudit
        (
            EMADonorID,
            EMAOrganizationID,
            DonorAuditPayload,
            ChangedByUserID,
            DateTimeCreated
        )
        SELECT
            donor.EMADonorID,
            donor.EMAOrganizationID,
            (
                SELECT
                    donor.EMADonorID,
                    donor.EMAOrganizationID,
                    donor.ChMSDonorID,
                    donor.EMADefaultCampusID,
                    donor.EmailAddress,
                    donor.GivingNumber,
                    donor.InteracDonorName,
                    donor.OfficialDonorName,
                    donor.PhoneNumber,
                    donor.Address,
                    donor.City,
                    donor.ProvinceStateCode,
                    donor.PostalCode,
                    donor.Country,
                    donor.DonorStatusID,
                    donor.IsDonorPilotingEtransfers,
                    donor.AcknowledgmentForManualTransactions,
                    donor.AcknowledgementForAutomatedTransactions,
                    CAST(1 AS bit) AS IsActive,
                    donor.DateTimeCreated,
                    donor.CreatedByUserID,
                    donor.DateTimeUpdated,
                    donor.UpdatedbyUserID
                FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
            ),
            @UpdatedByUserID,
            SYSUTCDATETIME()
        FROM dbo.Donor AS donor
        WHERE donor.EMADonorID = @EMADonorID
          AND donor.EMAOrganizationID = @EMAOrganizationID;

        IF @@ROWCOUNT = 0
        BEGIN
            THROW 52014, 'The donor was not found in the organization.', 1;
        END;

        UPDATE dbo.Donor
        SET
        InteracDonorName = @InteracDonorName,
        OfficialDonorName = @Name,
        EmailAddress = @EmailAddress,
        Address = @Address,
        City = @City,
        ProvinceStateCode = @ProvinceStateCode,
        PostalCode = @PostalCode,
        Country = @Country,
        PhoneNumber = @PhoneNumber,
        EMADefaultCampusID = @EMADefaultCampusID,
        DonorStatusID = @DonorStatusID,
        /*
            Absent or unrecognized leaves the donor in whatever pilot state
            they are already in, rather than silently removing them.
        */
        IsDonorPilotingEtransfers =
            ISNULL(NULLIF(@IsDonorPilotingEtransfers, ''), IsDonorPilotingEtransfers),

        /*
            Each acknowledgement setting is preserved independently so a
            partial caller cannot reset a preference it does not know about.
        */
        AcknowledgmentForManualTransactions =
            ISNULL(NULLIF(@AcknowledgmentForManualTransactions, ''), AcknowledgmentForManualTransactions),
        AcknowledgementForAutomatedTransactions =
            ISNULL(NULLIF(@AcknowledgementForAutomatedTransactions, ''), AcknowledgementForAutomatedTransactions),

        DateTimeUpdated = SYSUTCDATETIME(),
        UpdatedbyUserID = @UpdatedByUserID
        WHERE EMADonorID = @EMADonorID
          AND EMAOrganizationID = @EMAOrganizationID;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
        BEGIN
            ROLLBACK TRANSACTION;
        END;

        THROW;
    END CATCH;
END;
GO
