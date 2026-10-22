/*
    EMA Demo Org / sandbox integration verification
    -----------------------------------------------
    Run after:
      1. sp_UserSandbox.sql
      2. 20260819_SandboxDemoOrgCompatibility.sql
      3. 20260819_CreateEmaDemoOrganization.sql
      4. 20260821_AddEmaDemoJourneys.sql

    The script validates the permanent template, creates one disposable sandbox
    through dbo.sp_SandboxCreate, proves all organization-owned defaults resolve
    to daughter rows owned by that sandbox, verifies the cloned counts, and then
    deletes the disposable sandbox with dbo.sp_SandboxDeleteOne.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

DECLARE
    @DemoOrganizationID INT,
    @DemoUserID INT,
    @TestSandboxOrganizationID INT,
    @TestSandboxUserID INT,
    @CreatedByUserID INT = 1,
    @TestPhone NVARCHAR(20) = N'7805550188',
    @TestIPAddress VARCHAR(50) = 'ema-demo-verification',
    @TestEmail NVARCHAR(512) = N'ema-demo-verification@emailmoney.app';

SELECT TOP (1)
    @DemoOrganizationID = EMAOrganizationID
FROM dbo.Organization
WHERE OrganizationName = 'EMA Demo Org'
ORDER BY EMAOrganizationID;

IF @DemoOrganizationID IS NULL
    THROW 51200,
        'EMA Demo Org does not exist.',
        1;

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.Organization
    WHERE EMAOrganizationID = @DemoOrganizationID
      AND OrganizationStatusID = 5000
)
    THROW 51201,
        'EMA Demo Org must have OrganizationStatusID 5000.',
        1;

SELECT TOP (1)
    @DemoUserID = ou.UserID
FROM dbo.OrganizationUser ou
INNER JOIN dbo.[User] u
    ON u.UserID = ou.UserID
LEFT JOIN dbo.OrganizationUserRole ur
    ON ur.EMAOrganizationID = ou.EMAOrganizationID
   AND ur.UserID = ou.UserID
WHERE ou.EMAOrganizationID = @DemoOrganizationID
  AND u.IsActive = 1
GROUP BY ou.UserID
ORDER BY
    COUNT(DISTINCT ur.RoleID) DESC,
    ou.UserID;

IF @DemoUserID IS NULL
    THROW 51202,
        'EMA Demo Org does not have an active template user.',
        1;

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.OrganizationUserRole
    WHERE EMAOrganizationID = @DemoOrganizationID
      AND UserID = @DemoUserID
)
    THROW 51203,
        'EMA Demo Org template user does not have a role.',
        1;

IF
(
    SELECT COUNT(*)
    FROM dbo.Donation
    WHERE EMAOrganizationID = @DemoOrganizationID
) <> 100
    THROW 51204,
        'EMA Demo Org must contain exactly 100 donations.',
        1;

DECLARE @TemplateAnonymousDonorID INT =
(
    SELECT EMADonorIDForAnonymous
    FROM dbo.Organization
    WHERE EMAOrganizationID = @DemoOrganizationID
);

IF @TemplateAnonymousDonorID IS NULL
    THROW 51205,
        'EMA Demo Org does not have an anonymous donor configured.',
        1;

IF
(
    SELECT COUNT(*)
    FROM dbo.Donor
    WHERE EMAOrganizationID = @DemoOrganizationID
      AND EMADonorID <> @TemplateAnonymousDonorID
) <> 25
    THROW 51206,
        'EMA Demo Org must contain exactly 25 non-anonymous donors.',
        1;

IF DATEDIFF
(
    DAY,
    (
        SELECT MIN(DateTimeReceived)
        FROM dbo.Donation
        WHERE EMAOrganizationID = @DemoOrganizationID
    ),
    (
        SELECT MAX(DateTimeReceived)
        FROM dbo.Donation
        WHERE EMAOrganizationID = @DemoOrganizationID
    )
) < 700
    THROW 51207,
        'EMA Demo Org donation history does not span approximately two years.',
        1;

IF
(
    SELECT COUNT(*)
    FROM dbo.DonorJourney
    WHERE EMAOrganizationID = @DemoOrganizationID
) <> 45
    THROW 51220,
        'EMA Demo Org must contain exactly 45 donor journey entries.',
        1;

IF
(
    SELECT COUNT(*)
    FROM dbo.OrganizationJourney
    WHERE EMAOrganizationID = @DemoOrganizationID
      AND InteractionComments LIKE 'EMA-DEMO-JOURNEY-%'
) <> 8
    THROW 51221,
        'EMA Demo Org must contain exactly 8 seeded organization journey entries.',
        1;

/* Remove an old interrupted verification sandbox if one exists. */
DECLARE @OldVerificationSandboxID INT =
(
    SELECT TOP (1) EMAOrganizationID
    FROM dbo.Organization
    WHERE SandboxEtransferEmail = LOWER(@TestEmail)
      AND ISNULL(OrganizationStatusID, -1) = 0
    ORDER BY EMAOrganizationID DESC
);

IF @OldVerificationSandboxID IS NOT NULL
    EXEC dbo.sp_SandboxDeleteOne
        @SandboxOrganizationID = @OldVerificationSandboxID;

BEGIN TRY
    /*
        Do not wrap this call in INSERT ... EXEC. sp_SandboxCreate owns a
        transaction and must be allowed to roll it back if validation fails.
    */
    EXEC dbo.sp_SandboxCreate
        @TemplateOrganizationID = @DemoOrganizationID,
        @TemplateUserID = @DemoUserID,
        @PhoneNumber = @TestPhone,
        @SandboxIPAddress = @TestIPAddress,
        @SandboxEmail = @TestEmail,
        @CreatedByUserID = @CreatedByUserID,
        @MaxTemplateDonations = 100,
        @ActiveDays = 180;

    SELECT TOP (1)
        @TestSandboxOrganizationID =
            organization.EMAOrganizationID
    FROM dbo.Organization organization
    WHERE organization.SandboxEtransferEmail =
          LOWER(@TestEmail)
      AND ISNULL(
          organization.OrganizationStatusID,
          -1
      ) = 0
    ORDER BY organization.EMAOrganizationID DESC;

    SELECT TOP (1)
        @TestSandboxUserID =
            sandboxUser.UserID
    FROM dbo.[User] sandboxUser
    WHERE sandboxUser.SandboxOrganizationID =
          @TestSandboxOrganizationID
      AND sandboxUser.IsActive = 1
    ORDER BY sandboxUser.UserID;

    IF @TestSandboxOrganizationID IS NULL
        THROW 51208,
            'Sandbox verification creation did not return an organization.',
            1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.Organization
        WHERE EMAOrganizationID = @TestSandboxOrganizationID
          AND OrganizationStatusID = 0
    )
        THROW 51209,
            'Verification organization was not created as status 0.',
            1;

    IF
    (
        SELECT COUNT(*)
        FROM dbo.Donation
        WHERE EMAOrganizationID = @TestSandboxOrganizationID
    ) <> 100
        THROW 51210,
            'Verification sandbox did not clone exactly 100 donations.',
            1;

    IF
    (
        SELECT COUNT(*)
        FROM dbo.Donor
        WHERE EMAOrganizationID = @TestSandboxOrganizationID
    ) < 26
        THROW 51211,
            'Verification sandbox did not clone the demo donor population.',
            1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.[User]
        WHERE UserID = @TestSandboxUserID
          AND SandboxOrganizationID = @TestSandboxOrganizationID
          AND IsActive = 1
    )
        THROW 51212,
            'Verification sandbox user was not created correctly.',
            1;

    IF
    (
        SELECT COUNT(*)
        FROM dbo.DonorStatus
        WHERE EMAOrganizationId = @TestSandboxOrganizationID
    ) <>
    (
        SELECT COUNT(*)
        FROM dbo.DonorStatus
        WHERE EMAOrganizationId = @DemoOrganizationID
    )
        THROW 51222,
            'Verification sandbox did not clone the donor statuses.',
            1;

    IF
    (
        SELECT COUNT(*)
        FROM dbo.DonorJourneyActivityType
        WHERE EMAOrganizationID = @TestSandboxOrganizationID
    ) <>
    (
        SELECT COUNT(*)
        FROM dbo.DonorJourneyActivityType
        WHERE EMAOrganizationID = @DemoOrganizationID
    )
        THROW 51223,
            'Verification sandbox did not clone the donor journey activity types.',
            1;

    IF
    (
        SELECT COUNT(*)
        FROM dbo.DonorJourney
        WHERE EMAOrganizationID = @TestSandboxOrganizationID
    ) <>
    (
        SELECT COUNT(*)
        FROM dbo.DonorJourney
        WHERE EMAOrganizationID = @DemoOrganizationID
    )
        THROW 51224,
            'Verification sandbox did not clone the donor journey history.',
            1;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.DonorJourney journey
        LEFT JOIN dbo.Donor donor
            ON donor.EMAOrganizationID = @TestSandboxOrganizationID
           AND donor.EMADonorID = journey.EMADonorId
        LEFT JOIN dbo.DonorStatus donorStatus
            ON donorStatus.EMAOrganizationId = @TestSandboxOrganizationID
           AND donorStatus.DonorStatusID = journey.DonorStatusID
        LEFT JOIN dbo.DonorJourneyActivityType activityType
            ON activityType.EMAOrganizationID = @TestSandboxOrganizationID
           AND activityType.ActivityTypeID = journey.ActivityTypeID
        LEFT JOIN dbo.DonorJourneyActivityType followUpActivityType
            ON followUpActivityType.EMAOrganizationID = @TestSandboxOrganizationID
           AND followUpActivityType.ActivityTypeID =
               journey.FollowUpActivityTypeID
        WHERE journey.EMAOrganizationID = @TestSandboxOrganizationID
          AND
          (
              donor.EMADonorID IS NULL
              OR donorStatus.DonorStatusID IS NULL
              OR activityType.ActivityTypeID IS NULL
              OR
              (
                  journey.FollowUpActivityTypeID IS NOT NULL
                  AND followUpActivityType.ActivityTypeID IS NULL
              )
              OR journey.UserId <> @TestSandboxUserID
          )
    )
        THROW 51225,
            'A cloned donor journey does not reference the sandbox donor, status, activity types, and user.',
            1;

    IF
    (
        SELECT COUNT(*)
        FROM dbo.OrganizationJourney
        WHERE EMAOrganizationID = @TestSandboxOrganizationID
    ) <>
    (
        SELECT COUNT(*)
        FROM dbo.OrganizationJourney
        WHERE EMAOrganizationID = @DemoOrganizationID
    )
        THROW 51226,
            'Verification sandbox did not clone the organization journey history.',
            1;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.OrganizationJourney journey
        WHERE journey.EMAOrganizationID = @TestSandboxOrganizationID
          AND
          (
              journey.EMAUserID <> @TestSandboxUserID
              OR
              (
                  journey.FollowUpUserId IS NOT NULL
                  AND journey.FollowUpUserId <> @TestSandboxUserID
              )
          )
    )
        THROW 51227,
            'A cloned organization journey does not reference the sandbox user.',
            1;

    /*
        Fail if the normal Organization insert trigger created any extra user
        link for the sandbox. sp_SandboxCreate should be the only user creator.
    */
    IF
    (
        SELECT COUNT(DISTINCT ou.UserID)
        FROM dbo.OrganizationUser ou
        WHERE ou.EMAOrganizationID = @TestSandboxOrganizationID
    ) <> 1
        THROW 51213,
            'Sandbox organization has more than one linked user; check TR_Organization_AfterInsert sandbox guard.',
            1;

    DECLARE @DefaultChecks TABLE
    (
        ColumnName SYSNAME NOT NULL,
        TargetTable SYSNAME NOT NULL,
        TargetKey SYSNAME NOT NULL,
        EntityDescription VARCHAR(50) NOT NULL
    );

    INSERT INTO @DefaultChecks
    (
        ColumnName,
        TargetTable,
        TargetKey,
        EntityDescription
    )
    VALUES
        (N'EMADonorIDForAnonymous',                 N'Donor',                      N'EMADonorID',             'Donor'),
        (N'EMAEventIDForCreditDebitCards',          N'OrganizationEvent',          N'EMAEventID',             'Event'),
        (N'EMASourceIDForCreditDebitCards',         N'OrganizationDonationSource', N'EMADonationSourceID',    'DonationSource'),
        (N'EMAMethodOfGivingIDForCreditDebitCards', N'OrganizationMethodOfGiving', N'EMAMethodOfGivingID',    'Method'),
        (N'EMAFundIDForCreditDebitCards',           N'OrganizationFund',           N'EMAFundID',              'Fund'),
        (N'EMACampusIDForCreditDebitCards',         N'OrganizationCampus',         N'EMACampusID',            'Campus'),
        (N'EMAEventIDForEtransfers',                N'OrganizationEvent',          N'EMAEventID',             'Event'),
        (N'EMASourceIDForEtransfers',               N'OrganizationDonationSource', N'EMADonationSourceID',    'DonationSource'),
        (N'EMAMethodOfGivingIDForETransfers',       N'OrganizationMethodOfGiving', N'EMAMethodOfGivingID',    'Method'),
        (N'EMAFundIDForEtransfer',                  N'OrganizationFund',           N'EMAFundID',              'Fund'),
        (N'EMACampusIDForEtransfers',               N'OrganizationCampus',         N'EMACampusID',            'Campus'),
        (N'EMAEventIDForPADs',                      N'OrganizationEvent',          N'EMAEventID',             'Event'),
        (N'EMASourceIDForPADs',                     N'OrganizationDonationSource', N'EMADonationSourceID',    'DonationSource'),
        (N'EMAMethodOfGivingIDForPADs',             N'OrganizationMethodOfGiving', N'EMAMethodOfGivingID',    'Method'),
        (N'EMAFundIDForPADs',                       N'OrganizationFund',           N'EMAFundID',              'Fund'),
        (N'EMACampusIDForPADs',                     N'OrganizationCampus',         N'EMACampusID',            'Campus'),
        (N'EMAMethodOfGivingIDForCash',             N'OrganizationMethodOfGiving', N'EMAMethodOfGivingID',    'Method'),
        (N'EMAMethodOfGivingIDForCheques',          N'OrganizationMethodOfGiving', N'EMAMethodOfGivingID',    'Method');

    DECLARE
        @ColumnName SYSNAME,
        @TargetTable SYSNAME,
        @TargetKey SYSNAME,
        @EntityDescription VARCHAR(50),
        @Sql NVARCHAR(MAX),
        @IsInvalid BIT;

    DECLARE default_cursor CURSOR LOCAL FAST_FORWARD FOR
        SELECT
            ColumnName,
            TargetTable,
            TargetKey,
            EntityDescription
        FROM @DefaultChecks
        ORDER BY ColumnName;

    OPEN default_cursor;

    FETCH NEXT FROM default_cursor
        INTO
            @ColumnName,
            @TargetTable,
            @TargetKey,
            @EntityDescription;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        IF COL_LENGTH('dbo.Organization', @ColumnName) IS NOT NULL
           AND OBJECT_ID(N'dbo.' + @TargetTable, N'U') IS NOT NULL
        BEGIN
            SET @IsInvalid = 0;

            SET @Sql =
                N'SELECT @IsInvalid =
                    CASE
                        WHEN organization.' + QUOTENAME(@ColumnName) + N' IS NULL
                            THEN 0
                        WHEN EXISTS
                        (
                            SELECT 1
                            FROM dbo.' + QUOTENAME(@TargetTable) + N' target
                            WHERE target.' + QUOTENAME(@TargetKey) + N' =
                                  organization.' + QUOTENAME(@ColumnName) + N'
                              AND target.EMAOrganizationID = @OrganizationID
                        )
                            THEN 0
                        ELSE 1
                    END
                  FROM dbo.Organization organization
                  WHERE organization.EMAOrganizationID = @OrganizationID;';

            EXEC sys.sp_executesql
                @Sql,
                N'@OrganizationID INT, @IsInvalid BIT OUTPUT',
                @OrganizationID = @TestSandboxOrganizationID,
                @IsInvalid = @IsInvalid OUTPUT;

            IF @IsInvalid = 1
            BEGIN
                DECLARE @Message NVARCHAR(2048) =
                    CONCAT(
                        'Sandbox default ',
                        @ColumnName,
                        ' does not resolve to a ',
                        @EntityDescription,
                        ' owned by the sandbox organization.'
                    );

                THROW 51214, @Message, 1;
            END;
        END;

        FETCH NEXT FROM default_cursor
            INTO
                @ColumnName,
                @TargetTable,
                @TargetKey,
                @EntityDescription;
    END;

    CLOSE default_cursor;
    DEALLOCATE default_cursor;

    SELECT
        @DemoOrganizationID AS SandboxTemplateOrganizationID,
        @DemoUserID AS SandboxTemplateUserID,
        @TestSandboxOrganizationID AS VerifiedSandboxOrganizationID,
        @TestSandboxUserID AS VerifiedSandboxUserID,
        COUNT(DISTINCT donation.EMADonationID) AS DonationCount,
        COUNT(DISTINCT donor.EMADonorID) AS DonorCount,
        (
            SELECT COUNT(*)
            FROM dbo.DonorJourney
            WHERE EMAOrganizationID = @TestSandboxOrganizationID
        ) AS DonorJourneyCount,
        (
            SELECT COUNT(*)
            FROM dbo.OrganizationJourney
            WHERE EMAOrganizationID = @TestSandboxOrganizationID
        ) AS OrganizationJourneyCount,
        MIN(donation.DateTimeReceived) AS OldestDonation,
        MAX(donation.DateTimeReceived) AS NewestDonation
    FROM dbo.Organization organization
    LEFT JOIN dbo.Donation donation
        ON donation.EMAOrganizationID = organization.EMAOrganizationID
    LEFT JOIN dbo.Donor donor
        ON donor.EMAOrganizationID = organization.EMAOrganizationID
    WHERE organization.EMAOrganizationID = @TestSandboxOrganizationID;

    EXEC dbo.sp_SandboxDeleteOne
        @SandboxOrganizationID = @TestSandboxOrganizationID;

    SET @TestSandboxOrganizationID = NULL;
END TRY
BEGIN CATCH
    IF @TestSandboxOrganizationID IS NOT NULL
       AND EXISTS
       (
           SELECT 1
           FROM dbo.Organization
           WHERE EMAOrganizationID = @TestSandboxOrganizationID
             AND ISNULL(OrganizationStatusID, -1) = 0
       )
    BEGIN
        BEGIN TRY
            EXEC dbo.sp_SandboxDeleteOne
                @SandboxOrganizationID = @TestSandboxOrganizationID;
        END TRY
        BEGIN CATCH
            PRINT CONCAT(
                'Verification cleanup also failed: ',
                ERROR_MESSAGE()
            );
        END CATCH;
    END;

    THROW;
END CATCH;
GO
