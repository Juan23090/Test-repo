namespace EMA.Api.Tests.ScreenFeedback;

public sealed class ScreenFeedbackSourceContractTests
{
    [Fact]
    public void SupportTicketsAreScopedToMenuOrganizationAndOpenStatus()
    {
        var source = ReadRepositorySource();

        Assert.Contains("ticket.MenuItemID = @MenuItemID", source);
        Assert.Contains("ticket.SupportTicketStatusID < 99", source);
        Assert.Contains("ticket.EMAOrganizationID = @OrganizationID", source);
    }

    [Fact]
    public void SupportTicketQueriesUseExistingStatusTable()
    {
        var repositorySource = ReadRepositorySource();
        var verifierSource = File.ReadAllText(
            FindRepositoryFile(
                "src", "db", "VerifyTask39ScreenActions.sql"));

        Assert.Contains("dbo.SupportTicketStatus AS status", repositorySource);
        Assert.Contains("FROM dbo.SupportTicketStatus", repositorySource);
        Assert.DoesNotContain("dbo.SupportTicketStatusID", repositorySource);
        Assert.DoesNotContain("SupportTIcketStatusID", repositorySource);

        Assert.Contains("N'dbo.SupportTicketStatus'", verifierSource);
        Assert.Contains(
            "FROM dbo.SupportTicketStatus AS actual",
            verifierSource);
        Assert.DoesNotContain(
            "dbo.SupportTicketStatusID",
            verifierSource);
        Assert.DoesNotContain(
            "SupportTIcketStatusID",
            verifierSource);
    }

    [Fact]
    public void ScreenFeedbackCreatesPersistAuthenticatedOrganization()
    {
        var source = ReadRepositorySource();

        Assert.Contains("INSERT INTO dbo.FrequentlyAskedQuestions", source);
        Assert.Contains("INSERT INTO dbo.EnhancementRequest", source);
        Assert.Contains("INSERT INTO dbo.SupportTicket", source);
        Assert.Contains("EMAOrganizationID", source);
        Assert.Contains("@OrganizationID", source);
    }

    [Fact]
    public void FaqIconOnlyLoadsReviewedActiveFaqs()
    {
        var source = ReadRepositorySource();

        Assert.Contains("faq.IsReviewed", source);
        Assert.Contains("faq.IsActive", source);
        Assert.Contains("= 'Y'", source);
    }

    [Fact]
    public void EnhancementVoteUsesOrganizationAndEnhancementCompositeKey()
    {
        var source = ReadRepositorySource();

        Assert.Contains("WHERE OrganizationID = @OrganizationID", source);
        Assert.Contains("EnhancementRequestID =", source);
        Assert.Contains("@EnhancementRequestID", source);
    }

    [Fact]
    public void Task39DatabaseTimestampsUseUtc()
    {
        var source = ReadRepositorySource();

        var upperSource = source.ToUpperInvariant();
        Assert.DoesNotContain("GETDATE()", upperSource);
        Assert.Contains("GETUTCDATE()", upperSource);
    }

    [Fact]
    public void ScreenFeedbackAuthorizationUsesEffectiveRoleMenuItemPermissions()
    {
        var source = File.ReadAllText(
            FindRepositoryFile(
                "src", "api", "EMA.Api", "Services", "Security",
                "SqlScreenFeedbackAuthorizationService.cs"));

        Assert.Contains("dbo.RoleMenuItem", source);
        Assert.Contains("dbo.OrganizationUserRole", source);
        Assert.Contains("OrganizationUserStatusID", source);
        Assert.Contains("_boundaryValidator.PlatformOrganizationID", source);
        Assert.Contains("_boundaryValidator.PlatformAdministratorCategoryID", source);
        Assert.Contains(
            "organizationUserRole.EMAOrganizationID = @PlatformOrganizationID",
            source);
        Assert.DoesNotContain(
            "identity.EMAOrganizationID != InsiderOrganizationID",
            source);
    }

    [Fact]
    public void DailyReviewUsesTaskStatusContract()
    {
        var repositorySource = ReadRepositorySource();
        var timerSource = File.ReadAllText(
            FindRepositoryFile(
                "src", "api", "EMA.Api", "Functions", "ScreenFeedback",
                "EnhancementRequestReviewFunction.cs"));

        Assert.Contains("EnhancementInDevelopmentStatusID = 40", repositorySource);
        Assert.Contains("EnhancementVotedDownStatusID = 97", repositorySource);
        Assert.Contains("MaxDaysAtStatus", repositorySource);
        Assert.Contains("TimerTrigger(\"0 0 2 * * *\")", timerSource);
    }


    [Fact]
    public void Task39Point1CreatesJourneyAndFeedbackAsOneLinkedTransaction()
    {
        var source = ReadRepositorySource();

        Assert.Contains("INSERT INTO dbo.OrganizationJourney", source);
        Assert.Contains("OrganizationInteractionID", source);
        Assert.Contains("EnhancementRequestID, FAQID, SupportTicketID", source);
        Assert.Contains("FaqJourneyActivityTypeID = 372", source);
        Assert.Contains("EnhancementJourneyActivityTypeID = 371", source);
        Assert.Contains("SupportRequestJourneyActivityTypeID = 99", source);
        Assert.Contains("SET XACT_ABORT ON", source);
        Assert.Contains("BEGIN TRANSACTION", source);
        Assert.Contains("RollbackAsync", source);
    }

    [Fact]
    public void Task41GlobalFaqsRemainVisibleInOrganizationJourneyFeedback()
    {
        var source = ReadRepositorySource();
        var dtoSource = File.ReadAllText(
            FindRepositoryFile(
                "src", "api", "EMA.Api", "DTO", "ScreenFeedbackDTO.cs"));
        var appModelSource = File.ReadAllText(
            FindRepositoryFile(
                "src", "app", "EMA.App", "Models", "OrganizationJourneyFeedbackModels.cs"));

        Assert.Contains("WHEN faq.MenuItemID IS NULL THEN 'General'", source);
        Assert.Contains("LEFT JOIN dbo.MenuItem AS menuItem", source);
        Assert.Contains("MenuItemID = GetNullableInt32(reader, \"MenuItemID\")", source);
        Assert.Contains("public int? MenuItemID { get; set; }", dtoSource);
        Assert.Contains("public int? MenuItemID { get; set; }", appModelSource);
    }

    [Fact]
    public void Task39Point1OrganizationViewerKeepsHistoricalItemsRegardlessOfScreenStatus()
    {
        var source = ReadRepositorySource();

        Assert.Contains("GetOrganizationJourneyFeedbackAsync", source);
        Assert.Contains("faq.EMAOrganizationID = @OrganizationID", source);
        Assert.Contains("er.EMAOrganizationID = @OrganizationID", source);
        Assert.Contains("ticket.EMAOrganizationID = @OrganizationID", source);
    }

    private static string ReadRepositorySource() =>
        File.ReadAllText(
            FindRepositoryFile(
                "src", "api", "EMA.Api", "Services", "ScreenFeedback",
                "SqlScreenFeedbackRepository.cs"));

    private static string FindRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(
                new[] { directory.FullName }.Concat(segments).ToArray());

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the EMA repository root.");
    }
}
