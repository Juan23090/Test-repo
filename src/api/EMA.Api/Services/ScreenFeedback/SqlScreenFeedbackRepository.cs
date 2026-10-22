using System.Data;
using EMA.Api.DTOs;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace EMA.Api.Services.ScreenFeedback;

public sealed class SqlScreenFeedbackRepository :
    IScreenFeedbackRepository
{
    private const short InitialEnhancementStatusID = 10;
    private const short InitialSupportTicketStatusID = 10;
    private const short EnhancementInDevelopmentStatusID = 40;
    private const short EnhancementVotedDownStatusID = 97;
    private const short SupportRequestJourneyActivityTypeID = 99;
    private const short EnhancementJourneyActivityTypeID = 371;
    private const short FaqJourneyActivityTypeID = 372;

    private readonly string _connectionString;

    public SqlScreenFeedbackRepository(
        IConfiguration configuration)
    {
        _connectionString =
            configuration["SqlConnectionString"]
            ?? Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString.");
    }

    public async Task<bool> MenuItemExistsAsync(
        int menuItemID,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            await OpenConnectionAsync(
                cancellationToken);

        await using var command =
            new SqlCommand(
                "SELECT COUNT(1) FROM dbo.MenuItem WHERE MenuItemID = @MenuItemID;",
                connection);

        command.Parameters
            .Add(
                "@MenuItemID",
                SqlDbType.Int)
            .Value = menuItemID;

        return Convert.ToInt32(
                   await command.ExecuteScalarAsync(
                       cancellationToken)) > 0;
    }

    public async Task<ScreenActionSummaryDto>
        GetScreenActionSummaryAsync(
            int menuItemID,
            int organizationID,
            CancellationToken cancellationToken = default)
    {
        await using var connection =
            await OpenConnectionAsync(
                cancellationToken);

        const string sql = """
            SELECT
                faq.FAQID,
                faq.MenuItemID,
                faq.Question,
                faq.Answer
            FROM dbo.FrequentlyAskedQuestions AS faq
            WHERE faq.MenuItemID = @MenuItemID
              AND UPPER(LTRIM(RTRIM(faq.IsReviewed))) = 'Y'
              AND UPPER(LTRIM(RTRIM(faq.IsActive))) = 'Y'
            ORDER BY faq.Question, faq.FAQID;

            SELECT
                er.EnhancementRequestID,
                er.MenuItemID,
                er.Title,
                er.Description,
                er.EnhancementRequestStatusID,
                ers.Description AS StatusDescription,
                ers.IconColor,
                ers.MaxDaysAtStatus,
                er.DateTimeUpdated,
                COALESCE(votes.YesVotes, 0) AS YesVotes,
                COALESCE(votes.NoVotes, 0) AS NoVotes,
                currentVote.Vote AS OrganizationVote
            FROM dbo.EnhancementRequest AS er
            INNER JOIN dbo.EnhancementRequestStatus AS ers
                ON ers.EnhancementRequestStatusID =
                   er.EnhancementRequestStatusID
            OUTER APPLY
            (
                SELECT
                    SUM(CASE WHEN UPPER(LTRIM(RTRIM(v.Vote))) = 'Y' THEN 1 ELSE 0 END) AS YesVotes,
                    SUM(CASE WHEN UPPER(LTRIM(RTRIM(v.Vote))) = 'N' THEN 1 ELSE 0 END) AS NoVotes
                FROM dbo.EnhancementRequestVote AS v
                WHERE v.EnhancementRequestID =
                      er.EnhancementRequestID
            ) AS votes
            OUTER APPLY
            (
                SELECT TOP (1)
                    UPPER(LTRIM(RTRIM(v.Vote))) AS Vote
                FROM dbo.EnhancementRequestVote AS v
                WHERE v.EnhancementRequestID =
                      er.EnhancementRequestID
                  AND v.OrganizationID = @OrganizationID
            ) AS currentVote
            WHERE er.MenuItemID = @MenuItemID
              AND UPPER(LTRIM(RTRIM(ers.ShowIcon))) = 'Y'
            ORDER BY
                er.EnhancementRequestStatusID,
                er.DateTimeUpdated DESC,
                er.EnhancementRequestID DESC;

            SELECT
                ticket.SupportTicketID,
                ticket.MenuItemID,
                ticket.Title,
                ticket.Description,
                ticket.SupportTicketStatusID AS SupportTicketStatusID,
                status.Description AS StatusDescription,
                ticket.DateTimeUpdated
            FROM dbo.SupportTicket AS ticket
            INNER JOIN dbo.SupportTicketStatus AS status
                ON status.SupportTicketStatusID =
                   ticket.SupportTicketStatusID
            WHERE ticket.MenuItemID = @MenuItemID
              AND ticket.SupportTicketStatusID < 99
              AND ticket.EMAOrganizationID = @OrganizationID
            ORDER BY
                ticket.DateTimeUpdated DESC,
                ticket.SupportTicketID DESC;
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters
            .Add(
                "@MenuItemID",
                SqlDbType.Int)
            .Value = menuItemID;

        command.Parameters
            .Add(
                "@OrganizationID",
                SqlDbType.Int)
            .Value = organizationID;

        var result =
            new ScreenActionSummaryDto
            {
                MenuItemID = menuItemID
            };

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            result.Faqs.Add(
                new ScreenFaqDto
                {
                    FAQID = GetInt32(reader, "FAQID"),
                    MenuItemID = GetInt32(reader, "MenuItemID"),
                    Question = GetString(reader, "Question"),
                    Answer = GetString(reader, "Answer")
                });
        }

        if (await reader.NextResultAsync(
                cancellationToken))
        {
            while (await reader.ReadAsync(
                       cancellationToken))
            {
                result.Enhancements.Add(
                    new ScreenEnhancementDto
                    {
                        EnhancementRequestID =
                            GetInt32(
                                reader,
                                "EnhancementRequestID"),
                        MenuItemID =
                            GetInt32(
                                reader,
                                "MenuItemID"),
                        Title =
                            GetString(
                                reader,
                                "Title"),
                        Description =
                            GetString(
                                reader,
                                "Description"),
                        EnhancementRequestStatusID =
                            GetInt16(
                                reader,
                                "EnhancementRequestStatusID"),
                        StatusDescription =
                            GetString(
                                reader,
                                "StatusDescription"),
                        IconColor =
                            GetString(
                                reader,
                                "IconColor").Trim(),
                        MaxDaysAtStatus =
                            GetInt16(
                                reader,
                                "MaxDaysAtStatus"),
                        DateTimeUpdated =
                            GetDateTime(
                                reader,
                                "DateTimeUpdated"),
                        YesVotes =
                            GetInt32(
                                reader,
                                "YesVotes"),
                        NoVotes =
                            GetInt32(
                                reader,
                                "NoVotes"),
                        OrganizationVote =
                            GetNullableString(
                                reader,
                                "OrganizationVote")
                    });
            }
        }

        result.EnhancementIconColor =
            result.Enhancements
                .FirstOrDefault()
                ?.IconColor;

        if (await reader.NextResultAsync(
                cancellationToken))
        {
            while (await reader.ReadAsync(
                       cancellationToken))
            {
                result.SupportTickets.Add(
                    new ScreenSupportTicketDto
                    {
                        SupportTicketID =
                            GetInt32(
                                reader,
                                "SupportTicketID"),
                        MenuItemID =
                            GetInt32(
                                reader,
                                "MenuItemID"),
                        Title =
                            GetString(
                                reader,
                                "Title"),
                        Description =
                            GetString(
                                reader,
                                "Description"),
                        SupportTicketStatusID =
                            GetInt16(
                                reader,
                                "SupportTicketStatusID"),
                        StatusDescription =
                            GetString(
                                reader,
                                "StatusDescription"),
                        DateTimeUpdated =
                            GetDateTime(
                                reader,
                                "DateTimeUpdated")
                    });
            }
        }

        return result;
    }

    public async Task<OrganizationJourneyFeedbackSummaryDto>
        GetOrganizationJourneyFeedbackAsync(
            int organizationID,
            CancellationToken cancellationToken = default)
    {
        await using var connection =
            await OpenConnectionAsync(cancellationToken);

        const string sql = """
            SELECT
                faq.FAQID,
                faq.MenuItemID,
                faq.OrganizationInteractionID,
                CASE
                    WHEN faq.MenuItemID IS NULL THEN 'General'
                    ELSE COALESCE(
                        NULLIF(LTRIM(RTRIM(menuItem.ScreenTitle)), ''),
                        NULLIF(LTRIM(RTRIM(menuItem.MenuName)), ''),
                        'General')
                END AS ScreenTitle,
                faq.Question,
                faq.Answer,
                faq.IsReviewed,
                faq.IsActive,
                faq.DateTimeEntered
            FROM dbo.FrequentlyAskedQuestions AS faq
            LEFT JOIN dbo.MenuItem AS menuItem
                ON menuItem.MenuItemID = faq.MenuItemID
            WHERE faq.EMAOrganizationID = @OrganizationID
            ORDER BY faq.DateTimeEntered DESC, faq.FAQID DESC;

            SELECT
                er.EnhancementRequestID,
                er.MenuItemID,
                er.OrganizationInteractionID,
                COALESCE(
                    NULLIF(LTRIM(RTRIM(menuItem.ScreenTitle)), ''),
                    NULLIF(LTRIM(RTRIM(menuItem.MenuName)), ''),
                    '') AS ScreenTitle,
                er.Title,
                er.Description,
                status.Description AS StatusDescription,
                er.DateTimeEntered
            FROM dbo.EnhancementRequest AS er
            INNER JOIN dbo.MenuItem AS menuItem
                ON menuItem.MenuItemID = er.MenuItemID
            INNER JOIN dbo.EnhancementRequestStatus AS status
                ON status.EnhancementRequestStatusID = er.EnhancementRequestStatusID
            WHERE er.EMAOrganizationID = @OrganizationID
            ORDER BY er.DateTimeEntered DESC, er.EnhancementRequestID DESC;

            SELECT
                ticket.SupportTicketID,
                ticket.MenuItemID,
                ticket.OrganizationInteractionID,
                COALESCE(
                    NULLIF(LTRIM(RTRIM(menuItem.ScreenTitle)), ''),
                    NULLIF(LTRIM(RTRIM(menuItem.MenuName)), ''),
                    '') AS ScreenTitle,
                ticket.Title,
                ticket.Description,
                status.Description AS StatusDescription,
                ticket.DateTimeEntered
            FROM dbo.SupportTicket AS ticket
            INNER JOIN dbo.MenuItem AS menuItem
                ON menuItem.MenuItemID = ticket.MenuItemID
            INNER JOIN dbo.SupportTicketStatus AS status
                ON status.SupportTicketStatusID = ticket.SupportTicketStatusID
            WHERE ticket.EMAOrganizationID = @OrganizationID
            ORDER BY ticket.DateTimeEntered DESC, ticket.SupportTicketID DESC;
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@OrganizationID", SqlDbType.Int).Value = organizationID;

        var result = new OrganizationJourneyFeedbackSummaryDto();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            result.Faqs.Add(new OrganizationJourneyFaqDto
            {
                FAQID = GetInt32(reader, "FAQID"),
                MenuItemID = GetNullableInt32(reader, "MenuItemID"),
                OrganizationInteractionID = GetNullableInt32(reader, "OrganizationInteractionID"),
                ScreenTitle = GetString(reader, "ScreenTitle"),
                Question = GetString(reader, "Question"),
                Answer = GetString(reader, "Answer"),
                IsReviewed = GetYesNo(reader, "IsReviewed"),
                IsActive = GetYesNo(reader, "IsActive"),
                DateTimeEntered = GetDateTime(reader, "DateTimeEntered")
            });
        }

        if (await reader.NextResultAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Enhancements.Add(new OrganizationJourneyEnhancementDto
                {
                    EnhancementRequestID = GetInt32(reader, "EnhancementRequestID"),
                    MenuItemID = GetInt32(reader, "MenuItemID"),
                    OrganizationInteractionID = GetNullableInt32(reader, "OrganizationInteractionID"),
                    ScreenTitle = GetString(reader, "ScreenTitle"),
                    Title = GetString(reader, "Title"),
                    Description = GetString(reader, "Description"),
                    StatusDescription = GetString(reader, "StatusDescription"),
                    DateTimeEntered = GetDateTime(reader, "DateTimeEntered")
                });
            }
        }

        if (await reader.NextResultAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                result.SupportTickets.Add(new OrganizationJourneySupportTicketDto
                {
                    SupportTicketID = GetInt32(reader, "SupportTicketID"),
                    MenuItemID = GetInt32(reader, "MenuItemID"),
                    OrganizationInteractionID = GetNullableInt32(reader, "OrganizationInteractionID"),
                    ScreenTitle = GetString(reader, "ScreenTitle"),
                    Title = GetString(reader, "Title"),
                    Description = GetString(reader, "Description"),
                    StatusDescription = GetString(reader, "StatusDescription"),
                    DateTimeEntered = GetDateTime(reader, "DateTimeEntered")
                });
            }
        }

        return result;
    }

    public Task<int> CreateFaqAsync(
        int menuItemID,
        int organizationID,
        int userID,
        string question,
        string answer,
        CancellationToken cancellationToken = default) =>
        CreateFaqCoreAsync(
            menuItemID,
            organizationID,
            userID,
            question,
            answer,
            cancellationToken);

    public Task<int> CreateGlobalFaqAsync(
        int organizationID,
        int userID,
        string question,
        string answer,
        CancellationToken cancellationToken = default) =>
        CreateFaqCoreAsync(
            null,
            organizationID,
            userID,
            question,
            answer,
            cancellationToken);

    private async Task<int> CreateFaqCoreAsync(
        int? menuItemID,
        int organizationID,
        int userID,
        string question,
        string answer,
        CancellationToken cancellationToken)
    {
        await using var connection =
            await OpenConnectionAsync(
                cancellationToken);

        const string sql = """
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            BEGIN TRY
                INSERT INTO dbo.FrequentlyAskedQuestions
                (
                    MenuItemID, EMAOrganizationID, Question, Answer, IsReviewed, Comments, IsActive, CopyToPublic,
                    OrganizationInteractionID, DateTimeEntered, AskedByUserID, DateTimeUpdated, UpdatedByUserID
                )
                VALUES
                (
                    @MenuItemID, @OrganizationID, @Question, @Answer, 'N', NULL, 'N', 'N',
                    NULL, GETUTCDATE(), @UserID, NULL, NULL
                );

                DECLARE @FeedbackID INT = CONVERT(INT, SCOPE_IDENTITY());
                DECLARE @JourneyMenuItemID INT = @MenuItemID;

                IF @JourneyMenuItemID IS NULL
                BEGIN
                    DECLARE @JourneyMenuItemCount INT;

                    SELECT
                        @JourneyMenuItemCount = COUNT(*),
                        @JourneyMenuItemID = MIN(MenuItemID)
                    FROM dbo.MenuItem
                    WHERE LOWER(LTRIM(RTRIM(URL))) = @InsiderFaqRoute
                      AND IsActive = 'Y';

                    IF @JourneyMenuItemCount <> 1
                        THROW 51041, 'Task 41 requires exactly one active Work With FAQs MenuItem for global FAQ Journey logging.', 1;
                END;

                DECLARE @JourneyOrganizationStatusID SMALLINT;

                SELECT
                    @JourneyOrganizationStatusID = status.OrganizationStatusID
                FROM dbo.Organization AS organization
                INNER JOIN dbo.OrganizationStatus AS status
                    ON status.OrganizationStatusID =
                       TRY_CONVERT(
                           SMALLINT,
                           organization.OrganizationStatusID)
                WHERE organization.EMAOrganizationID =
                      @OrganizationID;

                IF @JourneyOrganizationStatusID IS NULL
                BEGIN
                    SELECT TOP (1)
                        @JourneyOrganizationStatusID =
                            journey.OrganizationStatusID
                    FROM dbo.OrganizationJourney AS journey
                    INNER JOIN dbo.OrganizationStatus AS status
                        ON status.OrganizationStatusID =
                           journey.OrganizationStatusID
                    WHERE journey.EMAOrganizationID =
                          @OrganizationID
                    ORDER BY
                        journey.ActivityDateTime DESC,
                        journey.OrganizationInteractionID DESC;
                END;

                IF @JourneyOrganizationStatusID IS NULL
                    THROW 51039, 'The organization does not have a valid status for Journey logging.', 1;

                INSERT INTO dbo.OrganizationJourney
                (
                    EMAOrganizationID, EMAUserID, MenuItemID, ActivityDateTime, ActivityTypeID,
                    EnhancementRequestID, FAQID, SupportTicketID, InteractionComments, OrganizationStatusID,
                    FollowUpDateTime, FollowUpActivityTypeID, FollowUpUserId
                )
                VALUES
                (
                    @OrganizationID, @UserID, @JourneyMenuItemID, GETUTCDATE(), @JourneyActivityTypeID,
                    NULL, @FeedbackID, NULL, @Question,
                    @JourneyOrganizationStatusID,
                    NULL, NULL, NULL
                );

                DECLARE @JourneyID INT = CONVERT(INT, SCOPE_IDENTITY());
                UPDATE dbo.FrequentlyAskedQuestions
                SET OrganizationInteractionID = @JourneyID
                WHERE FAQID = @FeedbackID;

                COMMIT TRANSACTION;
                SELECT @FeedbackID;
            END TRY
            BEGIN CATCH
                IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
                THROW;
            END CATCH;
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters
            .Add(
                "@MenuItemID",
                SqlDbType.Int)
            .Value = DbValue(menuItemID);

        command.Parameters
            .Add(
                "@OrganizationID",
                SqlDbType.Int)
            .Value = organizationID;

        command.Parameters
            .Add(
                "@Question",
                SqlDbType.VarChar,
                256)
            .Value = question;

        command.Parameters
            .Add(
                "@Answer",
                SqlDbType.VarChar,
                -1)
            .Value = answer;

        command.Parameters
            .Add(
                "@UserID",
                SqlDbType.Int)
            .Value = userID;

        command.Parameters
            .Add(
                "@JourneyActivityTypeID",
                SqlDbType.SmallInt)
            .Value = FaqJourneyActivityTypeID;

        command.Parameters
            .Add(
                "@InsiderFaqRoute",
                SqlDbType.VarChar,
                255)
            .Value = "/ww-faqs";

        return Convert.ToInt32(
            await command.ExecuteScalarAsync(
                cancellationToken));
    }

    public async Task<int> CreateEnhancementRequestAsync(
        int menuItemID,
        int organizationID,
        int userID,
        string title,
        string description,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            await OpenConnectionAsync(
                cancellationToken);

        const string sql = """
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            BEGIN TRY
                INSERT INTO dbo.EnhancementRequest
                (
                    MenuItemID, EMAOrganizationID, Title, Description, EMAComments, EnhancementRequestStatusID,
                    OrganizationInteractionID, DateTimeEntered, EnteredByUserID, DateTimeUpdated, UpdatedByUserID
                )
                VALUES
                (
                    @MenuItemID, @OrganizationID, @Title, @Description, NULL, @StatusID,
                    NULL, GETUTCDATE(), @UserID, GETUTCDATE(), @UserID
                );

                DECLARE @FeedbackID INT = CONVERT(INT, SCOPE_IDENTITY());

                DECLARE @JourneyOrganizationStatusID SMALLINT;

                SELECT
                    @JourneyOrganizationStatusID = status.OrganizationStatusID
                FROM dbo.Organization AS organization
                INNER JOIN dbo.OrganizationStatus AS status
                    ON status.OrganizationStatusID =
                       TRY_CONVERT(
                           SMALLINT,
                           organization.OrganizationStatusID)
                WHERE organization.EMAOrganizationID =
                      @OrganizationID;

                IF @JourneyOrganizationStatusID IS NULL
                BEGIN
                    SELECT TOP (1)
                        @JourneyOrganizationStatusID =
                            journey.OrganizationStatusID
                    FROM dbo.OrganizationJourney AS journey
                    INNER JOIN dbo.OrganizationStatus AS status
                        ON status.OrganizationStatusID =
                           journey.OrganizationStatusID
                    WHERE journey.EMAOrganizationID =
                          @OrganizationID
                    ORDER BY
                        journey.ActivityDateTime DESC,
                        journey.OrganizationInteractionID DESC;
                END;

                IF @JourneyOrganizationStatusID IS NULL
                    THROW 51039, 'The organization does not have a valid status for Journey logging.', 1;

                INSERT INTO dbo.OrganizationJourney
                (
                    EMAOrganizationID, EMAUserID, MenuItemID, ActivityDateTime, ActivityTypeID,
                    EnhancementRequestID, FAQID, SupportTicketID, InteractionComments, OrganizationStatusID,
                    FollowUpDateTime, FollowUpActivityTypeID, FollowUpUserId
                )
                VALUES
                (
                    @OrganizationID, @UserID, @MenuItemID, GETUTCDATE(), @JourneyActivityTypeID,
                    @FeedbackID, NULL, NULL, @Title,
                    @JourneyOrganizationStatusID,
                    NULL, NULL, NULL
                );

                DECLARE @JourneyID INT = CONVERT(INT, SCOPE_IDENTITY());
                UPDATE dbo.EnhancementRequest
                SET OrganizationInteractionID = @JourneyID
                WHERE EnhancementRequestID = @FeedbackID;

                COMMIT TRANSACTION;
                SELECT @FeedbackID;
            END TRY
            BEGIN CATCH
                IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
                THROW;
            END CATCH;
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters
            .Add(
                "@MenuItemID",
                SqlDbType.Int)
            .Value = menuItemID;

        command.Parameters
            .Add(
                "@OrganizationID",
                SqlDbType.Int)
            .Value = organizationID;

        command.Parameters
            .Add(
                "@Title",
                SqlDbType.VarChar,
                256)
            .Value = title;

        command.Parameters
            .Add(
                "@Description",
                SqlDbType.VarChar,
                -1)
            .Value = description;

        command.Parameters
            .Add(
                "@StatusID",
                SqlDbType.SmallInt)
            .Value = InitialEnhancementStatusID;

        command.Parameters
            .Add(
                "@UserID",
                SqlDbType.Int)
            .Value = userID;

        command.Parameters
            .Add(
                "@JourneyActivityTypeID",
                SqlDbType.SmallInt)
            .Value = EnhancementJourneyActivityTypeID;

        return Convert.ToInt32(
            await command.ExecuteScalarAsync(
                cancellationToken));
    }

    public async Task<int> CreateSupportTicketAsync(
        int menuItemID,
        int organizationID,
        int userID,
        string title,
        string description,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            await OpenConnectionAsync(
                cancellationToken);

        await using var transaction =
            (SqlTransaction)await connection
                .BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);

        try
        {
            const string nextIDSql = """
                SELECT
                    ISNULL(MAX(SupportTicketID), 0) + 1
                FROM dbo.SupportTicket WITH (UPDLOCK, HOLDLOCK);
                """;

            await using var idCommand =
                new SqlCommand(
                    nextIDSql,
                    connection,
                    transaction);

            var supportTicketID =
                Convert.ToInt32(
                    await idCommand.ExecuteScalarAsync(
                        cancellationToken));

            const string insertSql = """
                INSERT INTO dbo.SupportTicket
                (
                    MenuItemID,
                    EMAOrganizationID,
                    SupportTicketID,
                    Title,
                    Description,
                    EMAComments,
                    SupportTicketStatusID,
                    OrganizationInteractionID,
                    DateTimeEntered,
                    RequestedByUserID,
                    DateTimeUpdated,
                    UpdatedByEMAUserID
                )
                VALUES
                (
                    @MenuItemID,
                    @OrganizationID,
                    @SupportTicketID,
                    @Title,
                    @Description,
                    NULL,
                    @StatusID,
                    NULL,
                    GETUTCDATE(),
                    @UserID,
                    GETUTCDATE(),
                    @UserID
                );
                """;

            await using var insertCommand =
                new SqlCommand(
                    insertSql,
                    connection,
                    transaction);

            insertCommand.Parameters
                .Add(
                    "@MenuItemID",
                    SqlDbType.Int)
                .Value = menuItemID;

            insertCommand.Parameters
                .Add(
                    "@OrganizationID",
                    SqlDbType.Int)
                .Value = organizationID;

            insertCommand.Parameters
                .Add(
                    "@SupportTicketID",
                    SqlDbType.Int)
                .Value = supportTicketID;

            insertCommand.Parameters
                .Add(
                    "@Title",
                    SqlDbType.VarChar,
                    256)
                .Value = title;

            insertCommand.Parameters
                .Add(
                    "@Description",
                    SqlDbType.VarChar,
                    -1)
                .Value = description;

            insertCommand.Parameters
                .Add(
                    "@StatusID",
                    SqlDbType.SmallInt)
                .Value = InitialSupportTicketStatusID;

            insertCommand.Parameters
                .Add(
                    "@UserID",
                    SqlDbType.Int)
                .Value = userID;

            await insertCommand.ExecuteNonQueryAsync(
                cancellationToken);

            const string journeySql = """
                DECLARE @JourneyOrganizationStatusID SMALLINT;

                SELECT
                    @JourneyOrganizationStatusID = status.OrganizationStatusID
                FROM dbo.Organization AS organization
                INNER JOIN dbo.OrganizationStatus AS status
                    ON status.OrganizationStatusID =
                       TRY_CONVERT(
                           SMALLINT,
                           organization.OrganizationStatusID)
                WHERE organization.EMAOrganizationID =
                      @OrganizationID;

                IF @JourneyOrganizationStatusID IS NULL
                BEGIN
                    SELECT TOP (1)
                        @JourneyOrganizationStatusID =
                            journey.OrganizationStatusID
                    FROM dbo.OrganizationJourney AS journey
                    INNER JOIN dbo.OrganizationStatus AS status
                        ON status.OrganizationStatusID =
                           journey.OrganizationStatusID
                    WHERE journey.EMAOrganizationID =
                          @OrganizationID
                    ORDER BY
                        journey.ActivityDateTime DESC,
                        journey.OrganizationInteractionID DESC;
                END;

                IF @JourneyOrganizationStatusID IS NULL
                    THROW 51039, 'The organization does not have a valid status for Journey logging.', 1;

                INSERT INTO dbo.OrganizationJourney
                (
                    EMAOrganizationID, EMAUserID, MenuItemID, ActivityDateTime, ActivityTypeID,
                    EnhancementRequestID, FAQID, SupportTicketID, InteractionComments, OrganizationStatusID,
                    FollowUpDateTime, FollowUpActivityTypeID, FollowUpUserId
                )
                VALUES
                (
                    @OrganizationID, @UserID, @MenuItemID, GETUTCDATE(), @JourneyActivityTypeID,
                    NULL, NULL, @SupportTicketID, @Title,
                    @JourneyOrganizationStatusID,
                    NULL, NULL, NULL
                );

                SELECT CONVERT(INT, SCOPE_IDENTITY());
                """;

            await using var journeyCommand = new SqlCommand(journeySql, connection, transaction);
            journeyCommand.Parameters.Add("@OrganizationID", SqlDbType.Int).Value = organizationID;
            journeyCommand.Parameters.Add("@UserID", SqlDbType.Int).Value = userID;
            journeyCommand.Parameters.Add("@MenuItemID", SqlDbType.Int).Value = menuItemID;
            journeyCommand.Parameters.Add("@JourneyActivityTypeID", SqlDbType.SmallInt).Value = SupportRequestJourneyActivityTypeID;
            journeyCommand.Parameters.Add("@SupportTicketID", SqlDbType.Int).Value = supportTicketID;
            journeyCommand.Parameters.Add("@Title", SqlDbType.VarChar, 256).Value = title;

            var journeyID = Convert.ToInt32(await journeyCommand.ExecuteScalarAsync(cancellationToken));

            await using var linkCommand = new SqlCommand(
                "UPDATE dbo.SupportTicket SET OrganizationInteractionID = @JourneyID WHERE SupportTicketID = @SupportTicketID;",
                connection,
                transaction);
            linkCommand.Parameters.Add("@JourneyID", SqlDbType.Int).Value = journeyID;
            linkCommand.Parameters.Add("@SupportTicketID", SqlDbType.Int).Value = supportTicketID;
            await linkCommand.ExecuteNonQueryAsync(cancellationToken);

            await transaction.CommitAsync(
                cancellationToken);

            return supportTicketID;
        }
        catch
        {
            await transaction.RollbackAsync(
                cancellationToken);

            throw;
        }
    }

    public async Task<bool> SaveEnhancementVoteAsync(
        int enhancementRequestID,
        int organizationID,
        int userID,
        string vote,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            await OpenConnectionAsync(
                cancellationToken);

        const string sql = """
            IF NOT EXISTS
            (
                SELECT 1
                FROM dbo.EnhancementRequest
                WHERE EnhancementRequestID =
                      @EnhancementRequestID
            )
            BEGIN
                SELECT CAST(0 AS BIT);
                RETURN;
            END;

            UPDATE dbo.EnhancementRequestVote
            SET
                Vote = @Vote,
                DateTimeUpdated = GETUTCDATE(),
                UpdatedByUserID = @UserID
            WHERE OrganizationID = @OrganizationID
              AND EnhancementRequestID =
                  @EnhancementRequestID;

            IF @@ROWCOUNT = 0
            BEGIN
                INSERT INTO dbo.EnhancementRequestVote
                (
                    OrganizationID,
                    EnhancementRequestID,
                    Vote,
                    DateTimeCreated,
                    CreatedByUserID,
                    DateTimeUpdated,
                    UpdatedByUserID
                )
                VALUES
                (
                    @OrganizationID,
                    @EnhancementRequestID,
                    @Vote,
                    GETUTCDATE(),
                    @UserID,
                    GETUTCDATE(),
                    @UserID
                );
            END;

            SELECT CAST(1 AS BIT);
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters
            .Add(
                "@EnhancementRequestID",
                SqlDbType.Int)
            .Value = enhancementRequestID;

        command.Parameters
            .Add(
                "@OrganizationID",
                SqlDbType.Int)
            .Value = organizationID;

        command.Parameters
            .Add(
                "@UserID",
                SqlDbType.Int)
            .Value = userID;

        command.Parameters
            .Add(
                "@Vote",
                SqlDbType.Char,
                1)
            .Value = vote;

        return Convert.ToBoolean(
            await command.ExecuteScalarAsync(
                cancellationToken));
    }

    public async Task<IReadOnlyList<PublicFaqDto>>
        GetPublicFaqsAsync(
            CancellationToken cancellationToken = default)
    {
        await using var connection =
            await OpenConnectionAsync(
                cancellationToken);

        const string sql = """
            SELECT
                faq.FAQID,
                faq.MenuItemID,
                CASE
                    WHEN faq.MenuItemID IS NULL THEN 'General'
                    ELSE COALESCE(
                        NULLIF(LTRIM(RTRIM(menuItem.ScreenTitle)), ''),
                        NULLIF(LTRIM(RTRIM(menuItem.MenuName)), ''),
                        'General')
                END AS ScreenTitle,
                faq.Question,
                faq.Answer,
                faq.DateTimeUpdated
            FROM dbo.FrequentlyAskedQuestions AS faq
            LEFT JOIN dbo.MenuItem AS menuItem
                ON menuItem.MenuItemID = faq.MenuItemID
            WHERE UPPER(LTRIM(RTRIM(faq.IsReviewed))) = 'Y'
              AND UPPER(LTRIM(RTRIM(faq.IsActive))) = 'Y'
              AND UPPER(LTRIM(RTRIM(faq.CopyToPublic))) = 'Y'
              AND (faq.MenuItemID IS NULL OR menuItem.IsActive = 'Y')
            ORDER BY
                ScreenTitle,
                faq.Question,
                faq.FAQID;
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        var items =
            new List<PublicFaqDto>();

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            items.Add(
                new PublicFaqDto
                {
                    FAQID = GetInt32(reader, "FAQID"),
                    MenuItemID = GetNullableInt32(reader, "MenuItemID"),
                    ScreenTitle = GetString(reader, "ScreenTitle"),
                    Question = GetString(reader, "Question"),
                    Answer = GetString(reader, "Answer"),
                    DateTimeUpdated =
                        GetNullableDateTime(
                            reader,
                            "DateTimeUpdated")
                });
        }

        return items;
    }

    public async Task<IReadOnlyList<FaqAdminDto>>
        GetFaqAdminAsync(
            CancellationToken cancellationToken = default)
    {
        await using var connection =
            await OpenConnectionAsync(
                cancellationToken);

        const string sql = """
            SELECT
                faq.FAQID,
                faq.MenuItemID,
                CASE
                    WHEN faq.MenuItemID IS NULL THEN 'General'
                    ELSE COALESCE(
                        NULLIF(LTRIM(RTRIM(menuItem.ScreenTitle)), ''),
                        NULLIF(LTRIM(RTRIM(menuItem.MenuName)), ''),
                        'General')
                END AS ScreenTitle,
                faq.Question,
                faq.Answer,
                faq.Comments,
                faq.IsReviewed,
                faq.IsActive,
                faq.CopyToPublic,
                faq.DateTimeEntered,
                faq.DateTimeUpdated,
                faq.AskedByUserID,
                COALESCE(askedUser.UserName, askedUser.EmailAddress, '') AS AskedByUserName,
                faq.EMAOrganizationID AS OrganizationID,
                COALESCE(originOrganization.OrganizationName, '') AS OrganizationName,
                faq.UpdatedByUserID,
                COALESCE(updatedUser.UserName, updatedUser.EmailAddress, '') AS UpdatedByUserName
            FROM dbo.FrequentlyAskedQuestions AS faq
            LEFT JOIN dbo.MenuItem AS menuItem
                ON menuItem.MenuItemID = faq.MenuItemID
            LEFT JOIN dbo.[User] AS askedUser
                ON askedUser.UserID = faq.AskedByUserID
            LEFT JOIN dbo.[User] AS updatedUser
                ON updatedUser.UserID = faq.UpdatedByUserID
            LEFT JOIN dbo.Organization AS originOrganization
                ON originOrganization.EMAOrganizationID =
                   faq.EMAOrganizationID
            ORDER BY
                faq.DateTimeEntered DESC,
                faq.FAQID DESC;
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        var items =
            new List<FaqAdminDto>();

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            items.Add(
                new FaqAdminDto
                {
                    FAQID = GetInt32(reader, "FAQID"),
                    MenuItemID = GetNullableInt32(reader, "MenuItemID"),
                    ScreenTitle = GetString(reader, "ScreenTitle"),
                    Question = GetString(reader, "Question"),
                    Answer = GetString(reader, "Answer"),
                    Comments = GetNullableString(reader, "Comments"),
                    IsReviewed = GetYesNo(reader, "IsReviewed"),
                    IsActive = GetYesNo(reader, "IsActive"),
                    CopyToPublic = GetYesNo(reader, "CopyToPublic"),
                    DateTimeEntered = GetDateTime(reader, "DateTimeEntered"),
                    DateTimeUpdated = GetNullableDateTime(reader, "DateTimeUpdated"),
                    AskedByUserID = GetInt32(reader, "AskedByUserID"),
                    AskedByUserName = GetString(reader, "AskedByUserName"),
                    OrganizationID = GetNullableInt32(reader, "OrganizationID"),
                    OrganizationName = GetString(reader, "OrganizationName"),
                    UpdatedByUserID = GetNullableInt32(reader, "UpdatedByUserID"),
                    UpdatedByUserName = GetString(reader, "UpdatedByUserName")
                });
        }

        return items;
    }

    public async Task<ScreenFeedbackSaveResult> UpdateFaqAsync(
        int faqID,
        int userID,
        UpdateFaqAdminRequestDto request,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            await OpenConnectionAsync(
                cancellationToken);

        const string sql = """
            UPDATE dbo.FrequentlyAskedQuestions
            SET
                Question = @Question,
                Answer = @Answer,
                Comments = @Comments,
                IsReviewed = @IsReviewed,
                IsActive = @IsActive,
                CopyToPublic = @CopyToPublic,
                DateTimeUpdated = GETUTCDATE(),
                UpdatedByUserID = @UserID
            WHERE FAQID = @FAQID;
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters.Add("@FAQID", SqlDbType.Int).Value = faqID;
        command.Parameters.Add("@Question", SqlDbType.VarChar, 256).Value = request.Question;
        command.Parameters.Add("@Answer", SqlDbType.VarChar, -1).Value = request.Answer;
        command.Parameters.Add("@Comments", SqlDbType.VarChar, 256).Value = DbValue(request.Comments);
        command.Parameters.Add("@IsReviewed", SqlDbType.Char, 1).Value = request.IsReviewed ? "Y" : "N";
        command.Parameters.Add("@IsActive", SqlDbType.Char, 1).Value = request.IsActive ? "Y" : "N";
        command.Parameters.Add("@CopyToPublic", SqlDbType.Char, 1).Value = request.CopyToPublic ? "Y" : "N";
        command.Parameters.Add("@UserID", SqlDbType.Int).Value = userID;

        var rows =
            await command.ExecuteNonQueryAsync(
                cancellationToken);

        return rows == 0
            ? ScreenFeedbackSaveResult.NotFound
            : ScreenFeedbackSaveResult.Success;
    }

    public async Task<EnhancementAdminResponseDto>
        GetEnhancementAdminAsync(
            CancellationToken cancellationToken = default)
    {
        await using var connection =
            await OpenConnectionAsync(
                cancellationToken);

        const string sql = """
            SELECT
                EnhancementRequestStatusID,
                Description,
                ShowIcon,
                IconColor,
                MaxDaysAtStatus
            FROM dbo.EnhancementRequestStatus
            ORDER BY EnhancementRequestStatusID;

            SELECT
                er.EnhancementRequestID,
                er.MenuItemID,
                COALESCE(
                    NULLIF(LTRIM(RTRIM(menuItem.ScreenTitle)), ''),
                    NULLIF(LTRIM(RTRIM(menuItem.MenuName)), ''),
                    '') AS ScreenTitle,
                er.Title,
                er.Description,
                er.EMAComments,
                er.EnhancementRequestStatusID,
                status.Description AS StatusDescription,
                status.ShowIcon,
                status.IconColor,
                status.MaxDaysAtStatus,
                er.DateTimeEntered,
                er.DateTimeUpdated,
                er.EnteredByUserID,
                COALESCE(enteredUser.UserName, enteredUser.EmailAddress, '') AS EnteredByUserName,
                er.EMAOrganizationID AS OrganizationID,
                COALESCE(originOrganization.OrganizationName, '') AS OrganizationName,
                er.UpdatedByUserID,
                COALESCE(updatedUser.UserName, updatedUser.EmailAddress, '') AS UpdatedByUserName,
                COALESCE(votes.YesVotes, 0) AS YesVotes,
                COALESCE(votes.NoVotes, 0) AS NoVotes
            FROM dbo.EnhancementRequest AS er
            INNER JOIN dbo.EnhancementRequestStatus AS status
                ON status.EnhancementRequestStatusID =
                   er.EnhancementRequestStatusID
            INNER JOIN dbo.MenuItem AS menuItem
                ON menuItem.MenuItemID = er.MenuItemID
            LEFT JOIN dbo.[User] AS enteredUser
                ON enteredUser.UserID = er.EnteredByUserID
            LEFT JOIN dbo.[User] AS updatedUser
                ON updatedUser.UserID = er.UpdatedByUserID
            LEFT JOIN dbo.Organization AS originOrganization
                ON originOrganization.EMAOrganizationID =
                   er.EMAOrganizationID
            OUTER APPLY
            (
                SELECT
                    SUM(CASE WHEN UPPER(LTRIM(RTRIM(v.Vote))) = 'Y' THEN 1 ELSE 0 END) AS YesVotes,
                    SUM(CASE WHEN UPPER(LTRIM(RTRIM(v.Vote))) = 'N' THEN 1 ELSE 0 END) AS NoVotes
                FROM dbo.EnhancementRequestVote AS v
                WHERE v.EnhancementRequestID =
                      er.EnhancementRequestID
            ) AS votes
            ORDER BY
                er.DateTimeUpdated DESC,
                er.EnhancementRequestID DESC;
            """;

        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var response = new EnhancementAdminResponseDto();

        while (await reader.ReadAsync(cancellationToken))
        {
            response.Statuses.Add(
                new EnhancementStatusDto
                {
                    EnhancementRequestStatusID = GetInt16(reader, "EnhancementRequestStatusID"),
                    Description = GetString(reader, "Description"),
                    ShowIcon = GetYesNo(reader, "ShowIcon"),
                    IconColor = GetString(reader, "IconColor").Trim(),
                    MaxDaysAtStatus = GetInt16(reader, "MaxDaysAtStatus")
                });
        }

        if (await reader.NextResultAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                response.Items.Add(
                    new EnhancementAdminDto
                    {
                        EnhancementRequestID = GetInt32(reader, "EnhancementRequestID"),
                        MenuItemID = GetInt32(reader, "MenuItemID"),
                        ScreenTitle = GetString(reader, "ScreenTitle"),
                        Title = GetString(reader, "Title"),
                        Description = GetString(reader, "Description"),
                        EMAComments = GetNullableString(reader, "EMAComments"),
                        EnhancementRequestStatusID = GetInt16(reader, "EnhancementRequestStatusID"),
                        StatusDescription = GetString(reader, "StatusDescription"),
                        ShowIcon = GetYesNo(reader, "ShowIcon"),
                        IconColor = GetString(reader, "IconColor").Trim(),
                        MaxDaysAtStatus = GetInt16(reader, "MaxDaysAtStatus"),
                        DateTimeEntered = GetDateTime(reader, "DateTimeEntered"),
                        DateTimeUpdated = GetDateTime(reader, "DateTimeUpdated"),
                        EnteredByUserID = GetInt32(reader, "EnteredByUserID"),
                        EnteredByUserName = GetString(reader, "EnteredByUserName"),
                        OrganizationID = GetNullableInt32(reader, "OrganizationID"),
                        OrganizationName = GetString(reader, "OrganizationName"),
                        UpdatedByUserID = GetInt32(reader, "UpdatedByUserID"),
                        UpdatedByUserName = GetString(reader, "UpdatedByUserName"),
                        YesVotes = GetInt32(reader, "YesVotes"),
                        NoVotes = GetInt32(reader, "NoVotes")
                    });
            }
        }

        return response;
    }

    public async Task<ScreenFeedbackSaveResult> UpdateEnhancementAsync(
        int enhancementRequestID,
        int userID,
        UpdateEnhancementAdminRequestDto request,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        if (!await EnhancementStatusExistsAsync(
                connection,
                request.EnhancementRequestStatusID,
                cancellationToken))
        {
            return ScreenFeedbackSaveResult.InvalidStatus;
        }

        const string sql = """
            UPDATE dbo.EnhancementRequest
            SET
                Title = @Title,
                Description = @Description,
                EMAComments = @EMAComments,
                EnhancementRequestStatusID = @StatusID,
                DateTimeUpdated = GETUTCDATE(),
                UpdatedByUserID = @UserID
            WHERE EnhancementRequestID = @EnhancementRequestID;
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@EnhancementRequestID", SqlDbType.Int).Value = enhancementRequestID;
        command.Parameters.Add("@Title", SqlDbType.VarChar, 256).Value = request.Title;
        command.Parameters.Add("@Description", SqlDbType.VarChar, -1).Value = request.Description;
        command.Parameters.Add("@EMAComments", SqlDbType.VarChar, -1).Value = DbValue(request.EMAComments);
        command.Parameters.Add("@StatusID", SqlDbType.SmallInt).Value = request.EnhancementRequestStatusID;
        command.Parameters.Add("@UserID", SqlDbType.Int).Value = userID;

        var rows = await command.ExecuteNonQueryAsync(cancellationToken);
        return rows == 0
            ? ScreenFeedbackSaveResult.NotFound
            : ScreenFeedbackSaveResult.Success;
    }

    public async Task<SupportTicketAdminResponseDto>
        GetSupportTicketAdminAsync(
            CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        const string sql = """
            SELECT
                SupportTicketStatusID,
                Description
            FROM dbo.SupportTicketStatus
            ORDER BY SupportTicketStatusID;

            SELECT
                ticket.SupportTicketID,
                ticket.MenuItemID,
                COALESCE(
                    NULLIF(LTRIM(RTRIM(menuItem.ScreenTitle)), ''),
                    NULLIF(LTRIM(RTRIM(menuItem.MenuName)), ''),
                    '') AS ScreenTitle,
                ticket.Title,
                ticket.Description,
                ticket.EMAComments,
                ticket.SupportTicketStatusID AS SupportTicketStatusID,
                status.Description AS StatusDescription,
                ticket.DateTimeEntered,
                ticket.DateTimeUpdated,
                ticket.RequestedByUserID,
                COALESCE(requestedUser.UserName, requestedUser.EmailAddress, '') AS RequestedByUserName,
                ticket.EMAOrganizationID AS OrganizationID,
                COALESCE(originOrganization.OrganizationName, '') AS OrganizationName,
                ticket.UpdatedByEMAUserID,
                COALESCE(updatedUser.UserName, updatedUser.EmailAddress, '') AS UpdatedByUserName
            FROM dbo.SupportTicket AS ticket
            INNER JOIN dbo.SupportTicketStatus AS status
                ON status.SupportTicketStatusID =
                   ticket.SupportTicketStatusID
            INNER JOIN dbo.MenuItem AS menuItem
                ON menuItem.MenuItemID = ticket.MenuItemID
            LEFT JOIN dbo.[User] AS requestedUser
                ON requestedUser.UserID = ticket.RequestedByUserID
            LEFT JOIN dbo.[User] AS updatedUser
                ON updatedUser.UserID = ticket.UpdatedByEMAUserID
            LEFT JOIN dbo.Organization AS originOrganization
                ON originOrganization.EMAOrganizationID =
                   ticket.EMAOrganizationID
            ORDER BY
                ticket.DateTimeUpdated DESC,
                ticket.SupportTicketID DESC;
            """;

        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var response = new SupportTicketAdminResponseDto();

        while (await reader.ReadAsync(cancellationToken))
        {
            response.Statuses.Add(
                new SupportTicketStatusDto
                {
                    SupportTicketStatusID = GetInt16(reader, "SupportTicketStatusID"),
                    Description = GetString(reader, "Description")
                });
        }

        if (await reader.NextResultAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                response.Items.Add(
                    new SupportTicketAdminDto
                    {
                        SupportTicketID = GetInt32(reader, "SupportTicketID"),
                        MenuItemID = GetInt32(reader, "MenuItemID"),
                        ScreenTitle = GetString(reader, "ScreenTitle"),
                        Title = GetString(reader, "Title"),
                        Description = GetString(reader, "Description"),
                        EMAComments = GetNullableString(reader, "EMAComments"),
                        SupportTicketStatusID = GetInt16(reader, "SupportTicketStatusID"),
                        StatusDescription = GetString(reader, "StatusDescription"),
                        DateTimeEntered = GetDateTime(reader, "DateTimeEntered"),
                        DateTimeUpdated = GetDateTime(reader, "DateTimeUpdated"),
                        RequestedByUserID = GetInt32(reader, "RequestedByUserID"),
                        RequestedByUserName = GetString(reader, "RequestedByUserName"),
                        OrganizationID = GetNullableInt32(reader, "OrganizationID"),
                        OrganizationName = GetString(reader, "OrganizationName"),
                        UpdatedByEMAUserID = GetInt32(reader, "UpdatedByEMAUserID"),
                        UpdatedByUserName = GetString(reader, "UpdatedByUserName")
                    });
            }
        }

        return response;
    }

    public async Task<ScreenFeedbackSaveResult> UpdateSupportTicketAsync(
        int supportTicketID,
        int userID,
        UpdateSupportTicketAdminRequestDto request,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        if (!await SupportStatusExistsAsync(
                connection,
                request.SupportTicketStatusID,
                cancellationToken))
        {
            return ScreenFeedbackSaveResult.InvalidStatus;
        }

        const string sql = """
            UPDATE dbo.SupportTicket
            SET
                Title = @Title,
                Description = @Description,
                EMAComments = @EMAComments,
                SupportTicketStatusID = @StatusID,
                DateTimeUpdated = GETUTCDATE(),
                UpdatedByEMAUserID = @UserID
            WHERE SupportTicketID = @SupportTicketID;
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@SupportTicketID", SqlDbType.Int).Value = supportTicketID;
        command.Parameters.Add("@Title", SqlDbType.VarChar, 256).Value = request.Title;
        command.Parameters.Add("@Description", SqlDbType.VarChar, -1).Value = request.Description;
        command.Parameters.Add("@EMAComments", SqlDbType.VarChar, -1).Value = DbValue(request.EMAComments);
        command.Parameters.Add("@StatusID", SqlDbType.SmallInt).Value = request.SupportTicketStatusID;
        command.Parameters.Add("@UserID", SqlDbType.Int).Value = userID;

        var rows = await command.ExecuteNonQueryAsync(cancellationToken);
        return rows == 0
            ? ScreenFeedbackSaveResult.NotFound
            : ScreenFeedbackSaveResult.Success;
    }

    public async Task<int> ReviewExpiredEnhancementRequestsAsync(
        int automatedUserID,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        // Task 39 provides MaxDaysAtStatus and asks the 2 AM job to move requests
        // along. For the voting status, a strict yes-vote majority advances to
        // In Development; a tie/no majority moves to Voted Down.
        const string sql = """
            ;WITH VoteTotals AS
            (
                SELECT
                    request.EnhancementRequestID,
                    SUM(CASE WHEN UPPER(LTRIM(RTRIM(vote.Vote))) = 'Y' THEN 1 ELSE 0 END) AS YesVotes,
                    SUM(CASE WHEN UPPER(LTRIM(RTRIM(vote.Vote))) = 'N' THEN 1 ELSE 0 END) AS NoVotes
                FROM dbo.EnhancementRequest AS request
                LEFT JOIN dbo.EnhancementRequestVote AS vote
                    ON vote.EnhancementRequestID =
                       request.EnhancementRequestID
                GROUP BY request.EnhancementRequestID
            )
            UPDATE request
            SET
                EnhancementRequestStatusID =
                    CASE
                        WHEN COALESCE(votes.YesVotes, 0) >
                             COALESCE(votes.NoVotes, 0)
                            THEN @InDevelopmentStatusID
                        ELSE @VotedDownStatusID
                    END,
                DateTimeUpdated = GETUTCDATE(),
                UpdatedByUserID = @AutomatedUserID
            FROM dbo.EnhancementRequest AS request
            INNER JOIN dbo.EnhancementRequestStatus AS status
                ON status.EnhancementRequestStatusID =
                   request.EnhancementRequestStatusID
            LEFT JOIN VoteTotals AS votes
                ON votes.EnhancementRequestID =
                   request.EnhancementRequestID
            WHERE request.EnhancementRequestStatusID =
                  @VotingStatusID
              AND DATEADD(
                      DAY,
                      status.MaxDaysAtStatus,
                      request.DateTimeUpdated) <= GETUTCDATE();

            SELECT @@ROWCOUNT;
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@VotingStatusID", SqlDbType.SmallInt).Value = InitialEnhancementStatusID;
        command.Parameters.Add("@InDevelopmentStatusID", SqlDbType.SmallInt).Value = EnhancementInDevelopmentStatusID;
        command.Parameters.Add("@VotedDownStatusID", SqlDbType.SmallInt).Value = EnhancementVotedDownStatusID;
        command.Parameters.Add("@AutomatedUserID", SqlDbType.Int).Value = automatedUserID;

        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<bool> EnhancementStatusExistsAsync(
        SqlConnection connection,
        short statusID,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            "SELECT COUNT(1) FROM dbo.EnhancementRequestStatus WHERE EnhancementRequestStatusID = @StatusID;",
            connection);
        command.Parameters.Add("@StatusID", SqlDbType.SmallInt).Value = statusID;
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task<bool> SupportStatusExistsAsync(
        SqlConnection connection,
        short statusID,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            "SELECT COUNT(1) FROM dbo.SupportTicketStatus WHERE SupportTicketStatusID = @StatusID;",
            connection);
        command.Parameters.Add("@StatusID", SqlDbType.SmallInt).Value = statusID;
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private async Task<SqlConnection> OpenConnectionAsync(
        CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static object DbValue(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? DBNull.Value
            : value.Trim();

    private static object DbValue(int? value) =>
        value.HasValue
            ? (object)value.Value
            : DBNull.Value;

    private static string GetString(
        SqlDataReader reader,
        string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal)
            ? string.Empty
            : Convert.ToString(reader.GetValue(ordinal)) ?? string.Empty;
    }

    private static string? GetNullableString(
        SqlDataReader reader,
        string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal)
            ? null
            : Convert.ToString(reader.GetValue(ordinal));
    }

    private static int GetInt32(
        SqlDataReader reader,
        string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal)
            ? 0
            : Convert.ToInt32(reader.GetValue(ordinal));
    }

    private static int? GetNullableInt32(
        SqlDataReader reader,
        string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal)
            ? null
            : Convert.ToInt32(reader.GetValue(ordinal));
    }

    private static short GetInt16(
        SqlDataReader reader,
        string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal)
            ? (short)0
            : Convert.ToInt16(reader.GetValue(ordinal));
    }

    private static DateTime GetDateTime(
        SqlDataReader reader,
        string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal)
            ? DateTime.MinValue
            : AsUtcDateTime(Convert.ToDateTime(reader.GetValue(ordinal)));
    }

    private static DateTime? GetNullableDateTime(
        SqlDataReader reader,
        string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal)
            ? null
            : AsUtcDateTime(Convert.ToDateTime(reader.GetValue(ordinal)));
    }

    private static DateTime AsUtcDateTime(DateTime value) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static bool GetYesNo(
        SqlDataReader reader,
        string columnName)
    {
        return GetString(reader, columnName)
            .Trim()
            .Equals("Y", StringComparison.OrdinalIgnoreCase);
    }
}
