CREATE OR ALTER PROCEDURE dbo.sp_IngestDonationEmail
    @OrgID INT,
    @DonorName VARCHAR(255) = NULL,
    @DonorEmail VARCHAR(255) = NULL,
    @GivingNumber VARCHAR(255) = NULL,
    @Amount MONEY = NULL,
    @ReferenceNumber VARCHAR(255) = NULL,
    @RecipientEmailAddress VARCHAR(255) = NULL,
    @GlobalMessageID NVARCHAR(1024) = NULL,
    @EmailBlobID VARCHAR(36) = NULL,
    @DateTimeReceived DATETIME2(7) = NULL,
    @ShortComment VARCHAR(255) = NULL,
    @FinancialInstitution VARCHAR(255) = NULL,
    @PaymentKey VARCHAR(255) = NULL,
    @PaymentNotificationID VARCHAR(255) = NULL,
    @DonationSourceID INT = NULL,
    @CreatedByUserID INT = 1,
    @IsInteracEmail BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Step_NotInteracEmail INT = 5;
    DECLARE @Step_EmailAlreadyProcessed INT = 10;
    DECLARE @Step_MissingReferenceNumber INT = 15;
    DECLARE @Step_DuplicateReferenceNumber INT = 20;
    DECLARE @Step_MissingDonationAmount INT = 25;
    DECLARE @Step_RecipientOrgNotMatched INT = 30;
    DECLARE @Step_MissingDonorName INT = 40;
    DECLARE @Step_MissingDonationDate INT = 45;
    DECLARE @Step_InvalidOrgConfiguration INT = 50;
    DECLARE @Step_NewDonorCreated INT = 75;
    DECLARE @Step_UnableToMatchDonor INT = 80;
    DECLARE @Step_DefaultFundApplied INT = 88;

    DECLARE @HoldingOrgID INT = 0;
    DECLARE @HoldingDonorID INT = NULL;

    SET @DonorName =
        NULLIF(LTRIM(RTRIM(@DonorName)), '');

    SET @DonorEmail =
        NULLIF(LOWER(LTRIM(RTRIM(@DonorEmail))), '');

    SET @GivingNumber =
        NULLIF(LTRIM(RTRIM(@GivingNumber)), '');

    SET @ReferenceNumber =
        NULLIF(LTRIM(RTRIM(@ReferenceNumber)), '');

    SET @RecipientEmailAddress =
        NULLIF(
            LOWER(LTRIM(RTRIM(@RecipientEmailAddress))),
            ''
        );

    SET @GlobalMessageID =
        NULLIF(LTRIM(RTRIM(@GlobalMessageID)), '');

    SET @EmailBlobID =
        NULLIF(LTRIM(RTRIM(@EmailBlobID)), '');

    SET @ShortComment =
        NULLIF(LTRIM(RTRIM(@ShortComment)), '');

    SET @FinancialInstitution =
        NULLIF(LTRIM(RTRIM(@FinancialInstitution)), '');

    SET @PaymentKey =
        NULLIF(LTRIM(RTRIM(@PaymentKey)), '');

    SET @PaymentNotificationID =
        NULLIF(LTRIM(RTRIM(@PaymentNotificationID)), '');

    DECLARE @WasDateMissing BIT =
        CASE
            WHEN @DateTimeReceived IS NULL THEN 1
            ELSE 0
        END;

    IF @DateTimeReceived IS NULL
    BEGIN
        SET @DateTimeReceived =
            SYSUTCDATETIME();
    END;

    BEGIN TRY
        BEGIN TRANSACTION;

        IF NOT EXISTS
        (
            SELECT 1
            FROM dbo.Organization WITH (UPDLOCK, HOLDLOCK)
            WHERE EMAOrganizationID = @HoldingOrgID
        )
        BEGIN
            THROW 51990, 'EMAOrganizationID 0 must exist before email ingestion can log failed or unresolved emails.', 1;
        END;

        SELECT @HoldingDonorID =
            EMADonorIDForAnonymous
        FROM dbo.Organization WITH (UPDLOCK, HOLDLOCK)
        WHERE EMAOrganizationID = @HoldingOrgID;

        IF @HoldingDonorID IS NOT NULL
           AND NOT EXISTS
           (
               SELECT 1
               FROM dbo.Donor
               WHERE EMADonorID = @HoldingDonorID
                 AND EMAOrganizationID = @HoldingOrgID
           )
        BEGIN
            SET @HoldingDonorID = NULL;
        END;

        IF @HoldingDonorID IS NULL
        BEGIN
            SELECT TOP (1)
                @HoldingDonorID = EMADonorID
            FROM dbo.Donor WITH (UPDLOCK, HOLDLOCK)
            WHERE EMAOrganizationID = @HoldingOrgID
            ORDER BY EMADonorID;
        END;

        IF @HoldingDonorID IS NULL
        BEGIN
            INSERT INTO dbo.Donor
            (
                EMAOrganizationID,
                InteracDonorName,
                OfficialDonorName,
                DateTimeCreated,
                CreatedByUserID
            )
            VALUES
            (
                @HoldingOrgID,
                'EMA Unresolved Email',
                'EMA Unresolved Email',
                SYSUTCDATETIME(),
                @CreatedByUserID
            );

            SET @HoldingDonorID =
                CONVERT(INT, SCOPE_IDENTITY());
        END;

        DECLARE @ExistingDonationID BIGINT = NULL;
        DECLARE @ExistingDonationOrganizationID INT = NULL;
        DECLARE @ExistingDonationStepID INT = NULL;
        DECLARE @ExistingHasDistribution BIT = 0;

        SELECT TOP (1)
            @ExistingDonationID =
                donation.EMADonationID,
            @ExistingDonationOrganizationID =
                donation.EMAOrganizationID,
            @ExistingDonationStepID =
                CASE
                    WHEN @GlobalMessageID IS NOT NULL
                         AND donation.GlobalMessageID =
                             @GlobalMessageID
                        THEN @Step_EmailAlreadyProcessed
                    ELSE @Step_DuplicateReferenceNumber
                END
        FROM dbo.Donation AS donation
            WITH (UPDLOCK, HOLDLOCK)
        WHERE
        (
            @GlobalMessageID IS NOT NULL
            AND donation.GlobalMessageID =
                @GlobalMessageID
            AND donation.EMAOrganizationID IN
                (@HoldingOrgID, @OrgID)
        )
        OR
        (
            @ReferenceNumber IS NOT NULL
            AND donation.ReferenceNumber =
                @ReferenceNumber
            AND donation.EMAOrganizationID =
                @OrgID
        )
        ORDER BY
            CASE
                WHEN donation.EMAOrganizationID = @OrgID
                    THEN 0
                ELSE 1
            END,
            CASE
                WHEN @GlobalMessageID IS NOT NULL
                     AND donation.GlobalMessageID =
                         @GlobalMessageID
                    THEN 0
                ELSE 1
            END,
            donation.EMADonationID;

        IF @ExistingDonationID IS NOT NULL
           AND EXISTS
           (
               SELECT 1
               FROM dbo.DonationDistribution
               WHERE EMADonationID =
                     @ExistingDonationID
           )
        BEGIN
            SET @ExistingHasDistribution = 1;
        END;

        IF @ExistingDonationID IS NOT NULL
           AND
           (
               @ExistingDonationOrganizationID <>
                   @HoldingOrgID
               OR @OrgID = @HoldingOrgID
               OR @ExistingHasDistribution = 1
           )
        BEGIN
            IF NOT EXISTS
            (
                SELECT 1
                FROM dbo.DonationStepLog
                WHERE EMADonationID =
                      @ExistingDonationID
                  AND DonationStepID =
                      @ExistingDonationStepID
            )
            BEGIN
                INSERT INTO dbo.DonationStepLog
                (
                    EMADonationID,
                    DonationStepID,
                    DateTimeLogged
                )
                VALUES
                (
                    @ExistingDonationID,
                    @ExistingDonationStepID,
                    SYSUTCDATETIME()
                );
            END;

            COMMIT TRANSACTION;

            SELECT
                @ExistingDonationID AS NewDonationID,
                donation.EMADonorID,
                CAST(0 AS BIT) AS WasDonorCreated,
                CAST(1 AS BIT) AS WasDuplicate,
                CAST(NULL AS INT) AS EMAFundID,
                CAST(NULL AS INT) AS EMACampusID,
                CAST(NULL AS INT) AS EMAMethodOfGivingID,
                @ExistingDonationStepID AS DonationStepID,
                CONVERT(
                    BIT,
                    CASE
                        WHEN @ExistingDonationStepID =
                             @Step_EmailAlreadyProcessed
                            THEN 1
                        ELSE 0
                    END
                ) AS WasSuccessful
            FROM dbo.Donation AS donation
            WHERE donation.EMADonationID =
                  @ExistingDonationID
              AND donation.EMAOrganizationID =
                  @ExistingDonationOrganizationID;

            RETURN;
        END;

        DECLARE @WorkingDonationID BIGINT =
            @ExistingDonationID;

        IF @WorkingDonationID IS NULL
        BEGIN
            INSERT INTO dbo.Donation
            (
                EMAOrganizationID,
                EMADonorID,
                Comments,
                DateTimeReceived,
                DeadlineToMakeFundChanges,
                EMADonationSourceID,
                EMAMethodOfGivingID,
                EmailBlobID,
                GlobalMessageID,
                InteracDonorName,
                RecipientEmailAddress,
                ReferenceNumber,
                DonationAmount,
                IsTemplate,
                DateTimeCreated,
                CreatedbyUserID
            )
            VALUES
            (
                @HoldingOrgID,
                @HoldingDonorID,
                @ShortComment,
                @DateTimeReceived,
                NULL,
                NULL,
                NULL,
                @EmailBlobID,
                @GlobalMessageID,
                @DonorName,
                @RecipientEmailAddress,
                @ReferenceNumber,
                CASE
                    WHEN @Amount IS NOT NULL
                         AND @Amount > 0
                        THEN @Amount
                    ELSE NULL
                END,
                'N',
                SYSUTCDATETIME(),
                @CreatedByUserID
            );

            SET @WorkingDonationID =
                CONVERT(BIGINT, SCOPE_IDENTITY());
        END
        ELSE
        BEGIN
            UPDATE dbo.Donation
            SET
                EMAOrganizationID = @HoldingOrgID,
                EMADonorID = @HoldingDonorID,
                Comments = @ShortComment,
                DateTimeReceived = @DateTimeReceived,
                DeadlineToMakeFundChanges = NULL,
                EMADonationSourceID = NULL,
                EMAMethodOfGivingID = NULL,
                EmailBlobID =
                    COALESCE(@EmailBlobID, EmailBlobID),
                GlobalMessageID =
                    COALESCE(
                        @GlobalMessageID,
                        GlobalMessageID
                    ),
                InteracDonorName = @DonorName,
                RecipientEmailAddress =
                    @RecipientEmailAddress,
                ReferenceNumber = @ReferenceNumber,
                DonationAmount =
                    CASE
                        WHEN @Amount IS NOT NULL
                             AND @Amount > 0
                            THEN @Amount
                        ELSE NULL
                    END,
                DateTimeUpdated = SYSUTCDATETIME(),
                UpdatedbyUserID = @CreatedByUserID
            WHERE EMADonationID = @WorkingDonationID
              AND EMAOrganizationID IN
                  (@HoldingOrgID, @OrgID);
        END;

        IF @IsInteracEmail = 0
        BEGIN
            UPDATE dbo.Donation
            SET
                EMAOrganizationID = @HoldingOrgID,
                EMADonorID = @HoldingDonorID,
                Comments =
                    LEFT(
                        COALESCE(
                            @ShortComment,
                            'Not an Interac email.'
                        ),
                        255
                    ),
                DateTimeUpdated = SYSUTCDATETIME(),
                UpdatedbyUserID = @CreatedByUserID
            WHERE EMADonationID = @WorkingDonationID
              AND EMAOrganizationID = @HoldingOrgID;

            IF NOT EXISTS
            (
                SELECT 1
                FROM dbo.DonationStepLog
                WHERE EMADonationID =
                      @WorkingDonationID
                  AND DonationStepID =
                      @Step_NotInteracEmail
            )
            BEGIN
                INSERT INTO dbo.DonationStepLog
                (
                    EMADonationID,
                    DonationStepID,
                    DateTimeLogged
                )
                VALUES
                (
                    @WorkingDonationID,
                    @Step_NotInteracEmail,
                    SYSUTCDATETIME()
                );
            END;

            COMMIT TRANSACTION;

            SELECT
                @WorkingDonationID AS NewDonationID,
                @HoldingDonorID AS EMADonorID,
                CAST(0 AS BIT) AS WasDonorCreated,
                CAST(0 AS BIT) AS WasDuplicate,
                CAST(NULL AS INT) AS EMAFundID,
                CAST(NULL AS INT) AS EMACampusID,
                CAST(NULL AS INT) AS EMAMethodOfGivingID,
                @Step_NotInteracEmail AS DonationStepID,
                CAST(0 AS BIT) AS WasSuccessful;

            RETURN;
        END;

        DECLARE @FailureStepID INT = NULL;
        DECLARE @FailureComment VARCHAR(255) = NULL;

        IF @ReferenceNumber IS NULL
        BEGIN
            SET @FailureStepID =
                @Step_MissingReferenceNumber;

            SET @FailureComment =
                'Could not find Interac reference number.';
        END
        ELSE IF @Amount IS NULL
                OR @Amount <= 0
        BEGIN
            SET @FailureStepID =
                @Step_MissingDonationAmount;

            SET @FailureComment =
                'Could not find donation amount.';
        END
        ELSE IF @OrgID IS NULL
                OR @OrgID = @HoldingOrgID
                OR @RecipientEmailAddress IS NULL
                OR NOT EXISTS
                (
                    SELECT 1
                    FROM dbo.Organization
                    WHERE EMAOrganizationID = @OrgID
                      AND
                      (
                          LOWER(
                              LTRIM(
                                  RTRIM(
                                      EtransferEmailAddress
                                  )
                              )
                          ) = @RecipientEmailAddress
                          OR LOWER(
                              LTRIM(
                                  RTRIM(
                                      SandboxEtransferEmail
                                  )
                              )
                          ) = @RecipientEmailAddress
                      )
                )
        BEGIN
            SET @FailureStepID =
                @Step_RecipientOrgNotMatched;

            SET @FailureComment =
                'Recipient email address did not match an EMA organization.';
        END
        ELSE IF @DonorName IS NULL
        BEGIN
            SET @FailureStepID =
                @Step_MissingDonorName;

            SET @FailureComment =
                'Could not find donor name.';
        END
        ELSE IF @WasDateMissing = 1
        BEGIN
            SET @FailureStepID =
                @Step_MissingDonationDate;

            SET @FailureComment =
                'Could not find donation received date.';
        END;

        IF @FailureStepID IS NOT NULL
        BEGIN
            UPDATE dbo.Donation
            SET
                EMAOrganizationID = @HoldingOrgID,
                EMADonorID = @HoldingDonorID,
                Comments =
                    LEFT(
                        COALESCE(
                            @FailureComment,
                            @ShortComment
                        ),
                        255
                    ),
                DateTimeUpdated = SYSUTCDATETIME(),
                UpdatedbyUserID = @CreatedByUserID
            WHERE EMADonationID = @WorkingDonationID
              AND EMAOrganizationID = @HoldingOrgID;

            IF NOT EXISTS
            (
                SELECT 1
                FROM dbo.DonationStepLog
                WHERE EMADonationID =
                      @WorkingDonationID
                  AND DonationStepID =
                      @FailureStepID
            )
            BEGIN
                INSERT INTO dbo.DonationStepLog
                (
                    EMADonationID,
                    DonationStepID,
                    DateTimeLogged
                )
                VALUES
                (
                    @WorkingDonationID,
                    @FailureStepID,
                    SYSUTCDATETIME()
                );
            END;

            COMMIT TRANSACTION;

            SELECT
                @WorkingDonationID AS NewDonationID,
                @HoldingDonorID AS EMADonorID,
                CAST(0 AS BIT) AS WasDonorCreated,
                CAST(0 AS BIT) AS WasDuplicate,
                CAST(NULL AS INT) AS EMAFundID,
                CAST(NULL AS INT) AS EMACampusID,
                CAST(NULL AS INT) AS EMAMethodOfGivingID,
                @FailureStepID AS DonationStepID,
                CAST(0 AS BIT) AS WasSuccessful;

            RETURN;
        END;

        DECLARE @CampusID INT = NULL;
        DECLARE @FundID INT = NULL;
        DECLARE @MethodOfGivingID INT = NULL;
        DECLARE @MaxHoursToAllowFundChanges SMALLINT = 48;

        SELECT
            @CampusID =
                EMACampusIDForEtransfers,
            @FundID =
                EMAFundIDForEtransfer,
            @MethodOfGivingID =
                EMAMethodOfGivingIDForETransfers,
            @MaxHoursToAllowFundChanges =
                ISNULL(
                    MaxHoursToAllowFundChanges,
                    48
                )
        FROM dbo.Organization
        WHERE EMAOrganizationID = @OrgID;

        IF @FundID IS NULL
        BEGIN
            SELECT TOP (1)
                @FundID = EMAFundID,
                @CampusID =
                    COALESCE(
                        @CampusID,
                        EMACampusID
                    )
            FROM dbo.OrganizationFund
            WHERE EMAOrganizationID = @OrgID
              AND IsActive = 'Y'
            ORDER BY
                CASE
                    WHEN [Sequence] IS NULL THEN 1
                    ELSE 0
                END,
                [Sequence],
                EMAFundID;
        END
        ELSE
        BEGIN
            SELECT @CampusID =
                COALESCE(
                    @CampusID,
                    EMACampusID
                )
            FROM dbo.OrganizationFund
            WHERE EMAOrganizationID = @OrgID
              AND EMAFundID = @FundID;
        END;

        IF @FundID IS NULL
           OR NOT EXISTS
           (
               SELECT 1
               FROM dbo.OrganizationFund
               WHERE EMAOrganizationID = @OrgID
                 AND EMAFundID = @FundID
                 AND IsActive = 'Y'
           )
        BEGIN
            SET @FailureStepID =
                @Step_InvalidOrgConfiguration;

            SET @FailureComment =
                'Resolved organization default e-transfer fund is missing, inactive, or belongs to another organization.';
        END;

        IF @FailureStepID IS NULL
        BEGIN
            IF @CampusID IS NULL
            BEGIN
                SELECT TOP (1)
                    @CampusID = EMACampusID
                FROM dbo.OrganizationCampus
                WHERE EMAOrganizationID = @OrgID
                  AND IsActive = 'Y'
                ORDER BY
                    CASE
                        WHEN [Sequence] IS NULL THEN 1
                        ELSE 0
                    END,
                    [Sequence],
                    EMACampusID;
            END;

            IF @CampusID IS NULL
               OR NOT EXISTS
               (
                   SELECT 1
                   FROM dbo.OrganizationCampus
                   WHERE EMAOrganizationID = @OrgID
                     AND EMACampusID = @CampusID
                     AND IsActive = 'Y'
               )
            BEGIN
                SET @FailureStepID =
                    @Step_InvalidOrgConfiguration;

                SET @FailureComment =
                    'Resolved organization default e-transfer campus is missing, inactive, or belongs to another organization.';
            END;
        END;

        IF @FailureStepID IS NULL
           AND @MethodOfGivingID IS NOT NULL
           AND NOT EXISTS
           (
               SELECT 1
               FROM dbo.OrganizationMethodOfGiving
               WHERE EMAOrganizationID = @OrgID
                 AND EMAMethodOfGivingID =
                     @MethodOfGivingID
                 AND ISNULL(IsActive, 'Y') = 'Y'
           )
        BEGIN
            SET @FailureStepID =
                @Step_InvalidOrgConfiguration;

            SET @FailureComment =
                'Configured e-transfer method of giving is invalid for this organization.';
        END;

        IF @FailureStepID IS NULL
           AND @DonationSourceID IS NOT NULL
           AND NOT EXISTS
           (
               SELECT 1
               FROM dbo.OrganizationDonationSource
               WHERE EMAOrganizationID = @OrgID
                 AND EMADonationSourceID =
                     @DonationSourceID
                 AND ISNULL(IsActive, 'Y') = 'Y'
           )
        BEGIN
            SET @FailureStepID =
                @Step_InvalidOrgConfiguration;

            SET @FailureComment =
                'Supplied donation source is invalid for this organization.';
        END;

        IF @FailureStepID IS NOT NULL
        BEGIN
            UPDATE dbo.Donation
            SET
                EMAOrganizationID = @HoldingOrgID,
                EMADonorID = @HoldingDonorID,
                Comments =
                    LEFT(
                        COALESCE(
                            @FailureComment,
                            @ShortComment
                        ),
                        255
                    ),
                DateTimeUpdated = SYSUTCDATETIME(),
                UpdatedbyUserID = @CreatedByUserID
            WHERE EMADonationID = @WorkingDonationID
              AND EMAOrganizationID = @HoldingOrgID;

            IF NOT EXISTS
            (
                SELECT 1
                FROM dbo.DonationStepLog
                WHERE EMADonationID =
                      @WorkingDonationID
                  AND DonationStepID =
                      @FailureStepID
            )
            BEGIN
                INSERT INTO dbo.DonationStepLog
                (
                    EMADonationID,
                    DonationStepID,
                    DateTimeLogged
                )
                VALUES
                (
                    @WorkingDonationID,
                    @FailureStepID,
                    SYSUTCDATETIME()
                );
            END;

            COMMIT TRANSACTION;

            SELECT
                @WorkingDonationID AS NewDonationID,
                @HoldingDonorID AS EMADonorID,
                CAST(0 AS BIT) AS WasDonorCreated,
                CAST(0 AS BIT) AS WasDuplicate,
                @FundID AS EMAFundID,
                @CampusID AS EMACampusID,
                @MethodOfGivingID AS EMAMethodOfGivingID,
                @FailureStepID AS DonationStepID,
                CAST(0 AS BIT) AS WasSuccessful;

            RETURN;
        END;

        DECLARE @MatchedDonorID INT = NULL;
        DECLARE @WasDonorCreated BIT = 0;
        DECLARE @MatchCount INT = 0;

        IF @GivingNumber IS NOT NULL
        BEGIN
            SELECT
                @MatchCount = COUNT(*),
                @MatchedDonorID = MIN(EMADonorID)
            FROM dbo.Donor WITH (UPDLOCK, HOLDLOCK)
            WHERE EMAOrganizationID = @OrgID
              AND GivingNumber = @GivingNumber;

            IF @MatchCount > 1
            BEGIN
                SET @FailureStepID =
                    @Step_UnableToMatchDonor;

                SET @FailureComment =
                    'Multiple donor records match this giving number inside the organization.';
            END;
        END;

        IF @FailureStepID IS NULL
           AND @MatchedDonorID IS NULL
           AND @DonorEmail IS NOT NULL
        BEGIN
            SET @MatchCount = 0;

            SELECT
                @MatchCount = COUNT(*),
                @MatchedDonorID = MIN(EMADonorID)
            FROM dbo.Donor WITH (UPDLOCK, HOLDLOCK)
            WHERE EMAOrganizationID = @OrgID
              AND LOWER(LTRIM(RTRIM(EmailAddress))) =
                  @DonorEmail;

            IF @MatchCount > 1
            BEGIN
                SET @FailureStepID =
                    @Step_UnableToMatchDonor;

                SET @FailureComment =
                    'Multiple donor records match this email address inside the organization.';
            END;
        END;

        IF @FailureStepID IS NULL
           AND @MatchedDonorID IS NULL
           AND @DonorEmail IS NULL
           AND @DonorName IS NOT NULL
        BEGIN
            SET @MatchCount = 0;

            SELECT
                @MatchCount = COUNT(*),
                @MatchedDonorID = MIN(EMADonorID)
            FROM dbo.Donor WITH (UPDLOCK, HOLDLOCK)
            WHERE EMAOrganizationID = @OrgID
              AND InteracDonorName = @DonorName;

            IF @MatchCount > 1
            BEGIN
                SET @FailureStepID =
                    @Step_UnableToMatchDonor;

                SET @FailureComment =
                    'Multiple donor records match this Interac donor name and no email was supplied.';
            END;
        END;

        IF @FailureStepID IS NOT NULL
        BEGIN
            UPDATE dbo.Donation
            SET
                EMAOrganizationID = @HoldingOrgID,
                EMADonorID = @HoldingDonorID,
                Comments =
                    LEFT(
                        COALESCE(
                            @FailureComment,
                            @ShortComment
                        ),
                        255
                    ),
                DateTimeUpdated = SYSUTCDATETIME(),
                UpdatedbyUserID = @CreatedByUserID
            WHERE EMADonationID = @WorkingDonationID
              AND EMAOrganizationID = @HoldingOrgID;

            IF NOT EXISTS
            (
                SELECT 1
                FROM dbo.DonationStepLog
                WHERE EMADonationID =
                      @WorkingDonationID
                  AND DonationStepID =
                      @FailureStepID
            )
            BEGIN
                INSERT INTO dbo.DonationStepLog
                (
                    EMADonationID,
                    DonationStepID,
                    DateTimeLogged
                )
                VALUES
                (
                    @WorkingDonationID,
                    @FailureStepID,
                    SYSUTCDATETIME()
                );
            END;

            COMMIT TRANSACTION;

            SELECT
                @WorkingDonationID AS NewDonationID,
                @HoldingDonorID AS EMADonorID,
                CAST(0 AS BIT) AS WasDonorCreated,
                CAST(0 AS BIT) AS WasDuplicate,
                @FundID AS EMAFundID,
                @CampusID AS EMACampusID,
                @MethodOfGivingID AS EMAMethodOfGivingID,
                @FailureStepID AS DonationStepID,
                CAST(0 AS BIT) AS WasSuccessful;

            RETURN;
        END;

        IF @MatchedDonorID IS NULL
        BEGIN
            INSERT INTO dbo.Donor
            (
                EMAOrganizationID,
                EmailAddress,
                GivingNumber,
                InteracDonorName,
                DateTimeCreated,
                CreatedByUserID
            )
            VALUES
            (
                @OrgID,
                @DonorEmail,
                @GivingNumber,
                @DonorName,
                SYSUTCDATETIME(),
                @CreatedByUserID
            );

            SET @MatchedDonorID =
                CONVERT(INT, SCOPE_IDENTITY());

            SET @WasDonorCreated = 1;

            IF NOT EXISTS
            (
                SELECT 1
                FROM dbo.DonationStepLog
                WHERE EMADonationID =
                      @WorkingDonationID
                  AND DonationStepID =
                      @Step_NewDonorCreated
            )
            BEGIN
                INSERT INTO dbo.DonationStepLog
                (
                    EMADonationID,
                    DonationStepID,
                    DateTimeLogged
                )
                VALUES
                (
                    @WorkingDonationID,
                    @Step_NewDonorCreated,
                    SYSUTCDATETIME()
                );
            END;
        END
        ELSE
        BEGIN
            IF @DonorEmail IS NOT NULL
               AND EXISTS
               (
                   SELECT 1
                   FROM dbo.Donor WITH (UPDLOCK, HOLDLOCK)
                   WHERE EMAOrganizationID = @OrgID
                     AND LOWER(
                             LTRIM(
                                 RTRIM(EmailAddress)
                             )
                         ) = @DonorEmail
                     AND EMADonorID <> @MatchedDonorID
               )
            BEGIN
                UPDATE dbo.Donation
                SET
                    EMAOrganizationID = @HoldingOrgID,
                    EMADonorID = @HoldingDonorID,
                    Comments =
                        'Another donor in this organization already has this email address.',
                    DateTimeUpdated = SYSUTCDATETIME(),
                    UpdatedbyUserID = @CreatedByUserID
                WHERE EMADonationID =
                      @WorkingDonationID
                  AND EMAOrganizationID =
                      @HoldingOrgID;

                IF NOT EXISTS
                (
                    SELECT 1
                    FROM dbo.DonationStepLog
                    WHERE EMADonationID =
                          @WorkingDonationID
                      AND DonationStepID =
                          @Step_UnableToMatchDonor
                )
                BEGIN
                    INSERT INTO dbo.DonationStepLog
                    (
                        EMADonationID,
                        DonationStepID,
                        DateTimeLogged
                    )
                    VALUES
                    (
                        @WorkingDonationID,
                        @Step_UnableToMatchDonor,
                        SYSUTCDATETIME()
                    );
                END;

                COMMIT TRANSACTION;

                SELECT
                    @WorkingDonationID AS NewDonationID,
                    @HoldingDonorID AS EMADonorID,
                    CAST(0 AS BIT) AS WasDonorCreated,
                    CAST(0 AS BIT) AS WasDuplicate,
                    @FundID AS EMAFundID,
                    @CampusID AS EMACampusID,
                    @MethodOfGivingID AS EMAMethodOfGivingID,
                    @Step_UnableToMatchDonor AS DonationStepID,
                    CAST(0 AS BIT) AS WasSuccessful;

                RETURN;
            END;

            UPDATE dbo.Donor
            SET
                EmailAddress =
                    CASE
                        WHEN EmailAddress IS NULL
                             AND @DonorEmail IS NOT NULL
                            THEN @DonorEmail
                        ELSE EmailAddress
                    END,
                GivingNumber =
                    CASE
                        WHEN GivingNumber IS NULL
                             AND @GivingNumber IS NOT NULL
                            THEN @GivingNumber
                        ELSE GivingNumber
                    END,
                InteracDonorName =
                    CASE
                        WHEN InteracDonorName IS NULL
                             AND @DonorName IS NOT NULL
                            THEN @DonorName
                        ELSE InteracDonorName
                    END,
                DateTimeUpdated = SYSUTCDATETIME(),
                UpdatedbyUserID = @CreatedByUserID
            WHERE EMADonorID = @MatchedDonorID
              AND EMAOrganizationID = @OrgID;
        END;

        DECLARE @DeadlineToMakeFundChanges DATETIME2(7) =
            DATEADD(
                HOUR,
                @MaxHoursToAllowFundChanges,
                @DateTimeReceived
            );

        UPDATE dbo.Donation
        SET
            EMAOrganizationID = @OrgID,
            EMADonorID = @MatchedDonorID,
            Comments = @ShortComment,
            DateTimeReceived = @DateTimeReceived,
            DeadlineToMakeFundChanges =
                @DeadlineToMakeFundChanges,
            EMADonationSourceID = @DonationSourceID,
            EMAMethodOfGivingID = @MethodOfGivingID,
            EmailBlobID =
                COALESCE(@EmailBlobID, EmailBlobID),
            GlobalMessageID =
                COALESCE(
                    @GlobalMessageID,
                    GlobalMessageID
                ),
            InteracDonorName = @DonorName,
            RecipientEmailAddress =
                @RecipientEmailAddress,
            ReferenceNumber = @ReferenceNumber,
            DonationAmount = @Amount,
            IsTemplate = 'N',
            DateTimeUpdated = SYSUTCDATETIME(),
            UpdatedbyUserID = @CreatedByUserID
        WHERE EMADonationID = @WorkingDonationID
          AND EMAOrganizationID = @HoldingOrgID;

        IF @@ROWCOUNT = 0
        BEGIN
            THROW 51991, 'The staged donation changed tenants during ingestion.', 1;
        END;

        EXEC dbo.sp_ProcessBankDonationDistributions
            @EMADonationID = @WorkingDonationID;

        DECLARE @AppliedStepID INT = @Step_DefaultFundApplied;
        DECLARE @AppliedFundID INT = @FundID;
        DECLARE @AppliedCampusID INT = @CampusID;

        SELECT TOP (1)
            @AppliedStepID = DonationStepID
        FROM dbo.DonationStepLog
        WHERE EMADonationID = @WorkingDonationID
        ORDER BY DateTimeLogged DESC;

        SELECT TOP (1)
            @AppliedFundID = EMAFundID,
            @AppliedCampusID = EMACampusID
        FROM dbo.DonationDistribution
        WHERE EMADonationID = @WorkingDonationID
        ORDER BY EMADonationDistributionID ASC;

        DECLARE @AuditPayload NVARCHAR(MAX);

        SELECT @AuditPayload =
        (
            SELECT
                'DonationEmailIngested' AS EventType,
                @OrgID AS EMAOrganizationID,
                @MatchedDonorID AS EMADonorID,
                @WasDonorCreated AS WasDonorCreated,
                @DonorName AS DonorName,
                @DonorEmail AS DonorEmail,
                @GivingNumber AS GivingNumber,
                @Amount AS Amount,
                @ReferenceNumber AS ReferenceNumber,
                @RecipientEmailAddress AS RecipientEmailAddress,
                @GlobalMessageID AS GlobalMessageID,
                @EmailBlobID AS EmailBlobID,
                @FinancialInstitution AS FinancialInstitution,
                @PaymentKey AS PaymentKey,
                @PaymentNotificationID AS PaymentNotificationID,
                @AppliedFundID AS EMAFundID,
                @AppliedCampusID AS EMACampusID,
                @MethodOfGivingID AS EMAMethodOfGivingID,
                @WorkingDonationID AS EMADonationID,
                SYSUTCDATETIME() AS IngestedAtUtc
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
        );

        INSERT INTO dbo.DonationAudit
        (
            EMADonationID,
            DateTimeCreated,
            UpdatedByUserID,
            DDDDJsonPayload
        )
        VALUES
        (
            @WorkingDonationID,
            SYSUTCDATETIME(),
            @CreatedByUserID,
            @AuditPayload
        );

        COMMIT TRANSACTION;

        SELECT
            @WorkingDonationID AS NewDonationID,
            @MatchedDonorID AS EMADonorID,
            @WasDonorCreated AS WasDonorCreated,
            CAST(0 AS BIT) AS WasDuplicate,
            @AppliedFundID AS EMAFundID,
            @AppliedCampusID AS EMACampusID,
            @MethodOfGivingID AS EMAMethodOfGivingID,
            @AppliedStepID AS DonationStepID,
            CAST(1 AS BIT) AS WasSuccessful;
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
