/*
    EMA Demo Organization journey seed
    ----------------------------------
    Adds reusable donor and organization journey history to EMA Demo Org.

    Run after:
      - 20260819_CreateEmaDemoOrganization.sql
      - sp_UserSandbox.sql (updated version)

    The script is idempotent. Existing demo journey rows identified by their
    stable EMA-DEMO interaction IDs/comments are left unchanged.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

DECLARE
    @DemoOrganizationName VARCHAR(255) = 'EMA Demo Org',
    @DemoOrganizationID INT,
    @DemoUserID INT,
    @AnonymousDonorID INT,
    @Now DATETIME = GETDATE();

SELECT TOP (1)
    @DemoOrganizationID = o.EMAOrganizationID,
    @AnonymousDonorID = o.EMADonorIDForAnonymous
FROM dbo.Organization o
WHERE o.OrganizationName = @DemoOrganizationName
  AND o.OrganizationStatusID = 5000
ORDER BY o.EMAOrganizationID;

IF @DemoOrganizationID IS NULL
    THROW 51200,
        'EMA Demo Org with OrganizationStatusID 5000 was not found.',
        1;

SELECT TOP (1)
    @DemoUserID = ou.UserID
FROM dbo.OrganizationUser ou
INNER JOIN dbo.[User] u
    ON u.UserID = ou.UserID
INNER JOIN dbo.OrganizationUserRole ur
    ON ur.EMAOrganizationID = ou.EMAOrganizationID
   AND ur.UserID = ou.UserID
WHERE ou.EMAOrganizationID = @DemoOrganizationID
  AND u.IsActive = 1
ORDER BY
    CASE WHEN ur.RoleID = 10 THEN 0 ELSE 1 END,
    ou.UserID;

IF @DemoUserID IS NULL
    THROW 51201,
        'EMA Demo Org does not have an active template user with a role.',
        1;

/*
   DonorStatus.Description was historically NCHAR(10). The demo status names
   require the intended 20-character capacity.
*/
IF EXISTS
(
    SELECT 1
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = 'dbo'
      AND TABLE_NAME = 'DonorStatus'
      AND COLUMN_NAME = 'Description'
      AND CHARACTER_MAXIMUM_LENGTH < 20
)
BEGIN
    ALTER TABLE dbo.DonorStatus
    ALTER COLUMN [Description] NCHAR(20) NOT NULL;
END;

BEGIN TRANSACTION;

BEGIN TRY
    /* ---------------------------------------------------------------------
       Donor statuses
       --------------------------------------------------------------------- */

    /*
       DonorStatusID is globally unique even though DonorStatus is owned by an
       organization. StatusKey is used only inside this seed; new database IDs
       are allocated above the current global maximum.
    */
    DECLARE @DonorStatuses TABLE
    (
        StatusKey SMALLINT NOT NULL PRIMARY KEY,
        DonorStatusID SMALLINT NULL,
        [Sequence] SMALLINT NOT NULL,
        [Description] NCHAR(20) NOT NULL
    );

    INSERT INTO @DonorStatuses
    (
        StatusKey,
        [Sequence],
        [Description]
    )
    VALUES
        (10, 10, N'New Donor'),
        (20, 20, N'Engaged'),
        (30, 30, N'Recurring Donor'),
        (40, 40, N'Lapsed'),
        (50, 50, N'Major Donor');

    UPDATE source
    SET source.DonorStatusID =
        (
            SELECT TOP (1)
                target.DonorStatusID
            FROM dbo.DonorStatus target
            WHERE target.EMAOrganizationId = @DemoOrganizationID
              AND RTRIM(target.[Description]) =
                  RTRIM(source.[Description])
            ORDER BY target.DonorStatusID
        )
    FROM @DonorStatuses source;

    DECLARE
        @NextDonorStatusID INT,
        @StatusKey SMALLINT;

    SELECT
        @NextDonorStatusID =
            ISNULL(
                MAX(
                    CONVERT(
                        INT,
                        DonorStatusID
                    )
                ),
                0
            )
    FROM dbo.DonorStatus WITH
    (
        UPDLOCK,
        HOLDLOCK
    );

    DECLARE donor_status_cursor
        CURSOR LOCAL FAST_FORWARD FOR
        SELECT StatusKey
        FROM @DonorStatuses
        WHERE DonorStatusID IS NULL
        ORDER BY StatusKey;

    OPEN donor_status_cursor;

    FETCH NEXT FROM donor_status_cursor
        INTO @StatusKey;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @NextDonorStatusID += 1;

        IF @NextDonorStatusID > 32767
            THROW 51205,
                'No SMALLINT DonorStatusID values remain available.',
                1;

        UPDATE @DonorStatuses
        SET DonorStatusID =
            CONVERT(
                SMALLINT,
                @NextDonorStatusID
            )
        WHERE StatusKey = @StatusKey;

        FETCH NEXT FROM donor_status_cursor
            INTO @StatusKey;
    END;

    CLOSE donor_status_cursor;
    DEALLOCATE donor_status_cursor;

    UPDATE target
    SET
        target.[Sequence] = source.[Sequence],
        target.[Description] = source.[Description],
        target.IsActive = 1
    FROM dbo.DonorStatus target
    INNER JOIN @DonorStatuses source
        ON source.DonorStatusID = target.DonorStatusID
    WHERE target.EMAOrganizationId = @DemoOrganizationID;

    INSERT INTO dbo.DonorStatus
    (
        EMAOrganizationId,
        DonorStatusID,
        [Sequence],
        [Description],
        IsActive
    )
    SELECT
        @DemoOrganizationID,
        source.DonorStatusID,
        source.[Sequence],
        source.[Description],
        1
    FROM @DonorStatuses source
    WHERE source.DonorStatusID IS NOT NULL
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.DonorStatus target
          WHERE target.DonorStatusID = source.DonorStatusID
      );

    /* ---------------------------------------------------------------------
       Donor journey activity types
       --------------------------------------------------------------------- */

    /*
       Donor journey activity types use the same identifiers and descriptions
       exposed by the organization journey activity catalog. Existing
       organization-specific rows are preserved.
    */
    INSERT INTO dbo.DonorJourneyActivityType
    (
        EMAOrganizationID,
        ActivityTypeID,
        [Sequence],
        [Description],
        IsActive
    )
    SELECT
        @DemoOrganizationID,
        source.OrganizationJournalActivityTypeID,
        source.[Sequence],
        source.ActivityDescription,
        source.IsActive
    FROM dbo.OrganizationJourneyActivityType source
    WHERE source.IsActive = 'Y'
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.DonorJourneyActivityType target
          WHERE target.EMAOrganizationID = @DemoOrganizationID
            AND target.ActivityTypeID =
                source.OrganizationJournalActivityTypeID
      );

    /* ---------------------------------------------------------------------
       Donor journey history
       --------------------------------------------------------------------- */

    DECLARE @DemoDonors TABLE
    (
        DonorNumber INT NOT NULL PRIMARY KEY,
        EMADonorID INT NOT NULL UNIQUE
    );

    INSERT INTO @DemoDonors
    (
        DonorNumber,
        EMADonorID
    )
    SELECT
        ranked.DonorNumber,
        ranked.EMADonorID
    FROM
    (
        SELECT
            ROW_NUMBER() OVER
            (
                ORDER BY donor.EMADonorID
            ) AS DonorNumber,
            donor.EMADonorID
        FROM dbo.Donor donor
        WHERE donor.EMAOrganizationID = @DemoOrganizationID
          AND
          (
              @AnonymousDonorID IS NULL
              OR donor.EMADonorID <> @AnonymousDonorID
          )
    ) ranked
    WHERE ranked.DonorNumber <= 15;

    IF
    (
        SELECT COUNT(*)
        FROM @DemoDonors
    ) < 15
        THROW 51202,
            'EMA Demo Org must contain at least 15 non-anonymous donors before journeys are seeded.',
            1;

    DECLARE @DonorJourneySeed TABLE
    (
        DonorNumber INT NOT NULL,
        JourneyNumber INT NOT NULL,
        DonorInteractionId NCHAR(10) NOT NULL,
        ActivityDateTime DATETIME NOT NULL,
        ActivityTypeID INT NOT NULL,
        ActivityComments VARCHAR(256) NOT NULL,
        DonorStatusKey SMALLINT NOT NULL,
        FollowUpDateTime DATETIME NULL,
        FollowUpActivityTypeID SMALLINT NULL,
        PRIMARY KEY
        (
            DonorNumber,
            JourneyNumber
        )
    );

    INSERT INTO @DonorJourneySeed
    (
        DonorNumber,
        JourneyNumber,
        DonorInteractionId,
        ActivityDateTime,
        ActivityTypeID,
        ActivityComments,
        DonorStatusKey,
        FollowUpDateTime,
        FollowUpActivityTypeID
    )
    SELECT
        donor.DonorNumber,
        seed.JourneyNumber,
        CONVERT
        (
            NCHAR(10),
            LEFT
            (
                CONVERT
                (
                    VARCHAR(64),
                    HASHBYTES
                    (
                        'SHA2_256',
                        CONCAT
                        (
                            'EMA-DEMO:',
                            @DemoOrganizationID,
                            ':',
                            donor.EMADonorID,
                            ':',
                            seed.JourneyNumber
                        )
                    ),
                    2
                ),
                10
            )
        ),
        DATEADD
        (
            DAY,
            seed.ActivityDayOffset - donor.DonorNumber,
            @Now
        ),
        seed.ActivityTypeID,
        CONCAT
        (
            seed.ActivityComments,
            ' Demo donor ',
            donor.DonorNumber,
            '.'
        ),
        CASE
            WHEN seed.JourneyNumber = 3
             AND donor.DonorNumber % 5 = 0
                THEN 50
            ELSE seed.DonorStatusKey
        END,
        CASE
            WHEN seed.JourneyNumber = 3
             AND donor.DonorNumber % 3 = 0
                THEN DATEADD
                (
                    DAY,
                    14 + donor.DonorNumber,
                    @Now
                )
            ELSE DATEADD
                 (
                     DAY,
                     seed.FollowUpDayOffset - donor.DonorNumber,
                     @Now
                 )
        END,
        seed.FollowUpActivityTypeID
    FROM @DemoDonors donor
    CROSS JOIN
    (
        VALUES
            (1, -330, 210, 'Received an email asking about ways to give.',       10, -323, 220),
            (2, -190, 240, 'Completed a donor welcome and stewardship call.',    20, -176, 260),
            (3,  -45, 220, 'Sent a personalized giving summary and thank-you.',  30,  -31, 240)
    ) seed
    (
        JourneyNumber,
        ActivityDayOffset,
        ActivityTypeID,
        ActivityComments,
        DonorStatusKey,
        FollowUpDayOffset,
        FollowUpActivityTypeID
    );

    IF EXISTS
    (
        SELECT 1
        FROM @DonorJourneySeed
        WHERE LEN(RTRIM(DonorInteractionId)) > 10
           OR DATALENGTH(ActivityComments) > 256
    )
        THROW 51206,
            'A seeded donor journey exceeds the DonorJourney column limits.',
            1;

    INSERT INTO dbo.DonorJourney
    (
        EMAOrganizationID,
        EMADonorId,
        UserId,
        DonorInteractionId,
        ActivityDateTIme,
        ActivityTypeID,
        ActivityComments,
        DonorStatusID,
        FollowUpDateTIme,
        FollowUpActivityTypeID
    )
    SELECT
        @DemoOrganizationID,
        donor.EMADonorID,
        @DemoUserID,
        seed.DonorInteractionId,
        seed.ActivityDateTime,
        seed.ActivityTypeID,
        seed.ActivityComments,
        donorStatus.DonorStatusID,
        seed.FollowUpDateTime,
        seed.FollowUpActivityTypeID
    FROM @DonorJourneySeed seed
    INNER JOIN @DemoDonors donor
        ON donor.DonorNumber = seed.DonorNumber
    INNER JOIN @DonorStatuses donorStatus
        ON donorStatus.StatusKey = seed.DonorStatusKey
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.DonorJourney existing
        WHERE existing.DonorInteractionId =
              seed.DonorInteractionId
    );

    /* ---------------------------------------------------------------------
       Organization journey history
       --------------------------------------------------------------------- */

    DECLARE @OrganizationJourneySeed TABLE
    (
        JourneyNumber INT NOT NULL PRIMARY KEY,
        ActivityDayOffset INT NOT NULL,
        ActivityTypeID SMALLINT NOT NULL,
        InteractionComments VARCHAR(256) NOT NULL,
        OrganizationStatusID SMALLINT NOT NULL,
        FollowUpDayOffset INT NULL,
        FollowUpActivityTypeID SMALLINT NULL
    );

    INSERT INTO @OrganizationJourneySeed
    (
        JourneyNumber,
        ActivityDayOffset,
        ActivityTypeID,
        InteractionComments,
        OrganizationStatusID,
        FollowUpDayOffset,
        FollowUpActivityTypeID
    )
    VALUES
        (1, -360, 10,  'EMA-DEMO-JOURNEY-01: Organization requested an EMA demonstration.',            10,   -353, 15),
        (2, -340, 210, 'EMA-DEMO-JOURNEY-02: Received questions about donation processing by email.', 10,   -333, 15),
        (3, -300, 20,  'EMA-DEMO-JOURNEY-03: Organization began its sandbox trial.',                   20,   -286, 25),
        (4, -245, 240, 'EMA-DEMO-JOURNEY-04: Completed a sandbox follow-up phone call.',               20,   -231, 25),
        (5, -190, 30,  'EMA-DEMO-JOURNEY-05: Organization began a full-data trial.',                   30,   -176, 35),
        (6, -130, 220, 'EMA-DEMO-JOURNEY-06: Sent production setup instructions by email.',            30,   -116, 35),
        (7,  -75, 40,  'EMA-DEMO-JOURNEY-07: Organization completed its production setup.',            40,    -61, 45),
        (8,  -30, 50,  'EMA-DEMO-JOURNEY-08: Organization became an active EMA organization.',       5000,     14, 55);

    INSERT INTO dbo.OrganizationJourney
    (
        EMAOrganizationID,
        EMAUserID,
        ActivityDateTime,
        ActivityTypeID,
        InteractionComments,
        OrganizationStatusID,
        FollowUpDateTime,
        FollowUpActivityTypeID,
        FollowUpUserId
    )
    SELECT
        @DemoOrganizationID,
        @DemoUserID,
        DATEADD
        (
            DAY,
            seed.ActivityDayOffset,
            @Now
        ),
        seed.ActivityTypeID,
        seed.InteractionComments,
        seed.OrganizationStatusID,
        CASE
            WHEN seed.FollowUpDayOffset IS NULL
                THEN NULL
            ELSE DATEADD
                 (
                     DAY,
                     seed.FollowUpDayOffset,
                     @Now
                 )
        END,
        seed.FollowUpActivityTypeID,
        CASE
            WHEN seed.FollowUpActivityTypeID IS NULL
                THEN NULL
            ELSE @DemoUserID
        END
    FROM @OrganizationJourneySeed seed
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.OrganizationJourney existing
        WHERE existing.EMAOrganizationID = @DemoOrganizationID
          AND existing.InteractionComments = seed.InteractionComments
    );

    IF
    (
        SELECT COUNT(*)
        FROM dbo.DonorJourney journey
        INNER JOIN @DonorJourneySeed seed
            ON seed.DonorInteractionId =
               journey.DonorInteractionId
        WHERE journey.EMAOrganizationID = @DemoOrganizationID
    ) <> 45
        THROW 51203,
            'EMA Demo Org must contain exactly 45 seeded donor journey entries.',
            1;

    IF
    (
        SELECT COUNT(*)
        FROM dbo.OrganizationJourney
        WHERE EMAOrganizationID = @DemoOrganizationID
          AND InteractionComments LIKE 'EMA-DEMO-JOURNEY-%'
    ) <> 8
        THROW 51204,
            'EMA Demo Org must contain exactly 8 seeded organization journey entries.',
            1;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;

SELECT
    @DemoOrganizationID AS SandboxTemplateOrganizationID,
    (
        SELECT COUNT(*)
        FROM dbo.DonorStatus
        WHERE EMAOrganizationId = @DemoOrganizationID
    ) AS DonorStatuses,
    (
        SELECT COUNT(*)
        FROM dbo.DonorJourneyActivityType
        WHERE EMAOrganizationID = @DemoOrganizationID
    ) AS DonorJourneyActivityTypes,
    (
        SELECT COUNT(*)
        FROM dbo.DonorJourney
        WHERE EMAOrganizationID = @DemoOrganizationID
    ) AS DonorJourneys,
    (
        SELECT COUNT(*)
        FROM dbo.OrganizationJourney
        WHERE EMAOrganizationID = @DemoOrganizationID
    ) AS OrganizationJourneys;
GO
