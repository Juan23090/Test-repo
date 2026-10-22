namespace EMA.Api.Tests.MailingLists;

public sealed class MailingListsTenantIsolationSourceTests
{
    private readonly string _repositoryRoot = FindRepositoryRoot();

    [Fact]
    public void CsvExport_IsScopedToOrganizationInRouteAndSql()
    {
        var source = ReadRepositoryFile(
            "src", "api", "EMA.Api", "Functions", "MailingLists", "MailingListsFunctions.cs");

        Assert.Contains(
            "Route = \"mailing-lists/{orgID:int}/instance/{instanceID:int}/export\"",
            source);
        Assert.Contains("mli.OrganizationID = @OrgID", source);
        Assert.Contains("ml.EMAOrganizationID = @OrgID", source);
        Assert.Contains("instanceOwnershipSql", source);
    }

    [Fact]
    public void ListAndDonorMutations_ValidateTenantOwnership()
    {
        var source = ReadRepositoryFile(
            "src", "api", "EMA.Api", "Functions", "MailingLists", "MailingListsFunctions.cs");

        Assert.Contains("MailingListBelongsToOrganizationAsync(connection, orgID, listID)", source);
        Assert.Contains("DonorBelongsToOrganizationAsync(connection, orgID, donorID)", source);
        Assert.Contains("ml.EMAOrganizationID = @OrgID", source);
    }

    [Fact]
    public void AuditAttribution_ComesOnlyFromAuthenticatedIdentity()
    {
        var source = ReadRepositoryFile(
            "src", "api", "EMA.Api", "Functions", "MailingLists", "MailingListsFunctions.cs");
        var model = ReadRepositoryFile(
            "src", "app", "EMA.App", "Models", "MailingListModels.cs");

        Assert.Equal(2, CountOccurrences(source, "request.GetRequiredApiIdentity().UserID"));
        Assert.DoesNotContain("TryGetProperty(\"userID\"", source);
        Assert.DoesNotContain("UpdatedByUserID = 1", source);
        Assert.DoesNotContain("UserID", model);
    }

    [Fact]
    public void MailingListDetails_DownloadsExportThroughAuthenticatedHttpClient()
    {
        var component = ReadRepositoryFile(
            "src", "app", "EMA.App", "Components", "Pages", "Operations", "MailingListDetails.razor");
        var service = ReadRepositoryFile(
            "src", "app", "EMA.App", "Services", "MailingListService.cs");

        Assert.DoesNotContain("http://localhost:7071", component);
        Assert.Contains("ExportMailingListInstanceAsync", component);
        Assert.Contains("mailing-lists/{orgID}/instance/{instanceID}/export", service);
    }

    [Fact]
    public void MailingListPages_DoNotFallBackToOrganizationOne()
    {
        var pages = new[]
        {
            "CreateMailingList.razor",
            "MailingListDetails.razor",
            "MailingListReviewDonors.razor",
            "MailingListsIndex.razor"
        };

        foreach (var page in pages)
        {
            var source = ReadRepositoryFile(
                "src", "app", "EMA.App", "Components", "Pages", "Operations", page);
            Assert.DoesNotContain("EMAOrganizationID : 1", source);
        }
    }

    [Fact]
    public void PrepareMailingListInstance_ChecksAllCriteria2FieldsForAndEvaluation()
    {
        var sql = ReadRepositoryFile("src", "db", "sp_PrepareMailingListInstance.sql");

        Assert.Contains("@Donation2StartDate IS NOT NULL", sql);
        Assert.Contains("@Donation2EndDate IS NOT NULL", sql);
        Assert.Contains("@Donation2MoneyFrom IS NOT NULL", sql);
        Assert.Contains("@Donation2MoneyTo IS NOT NULL", sql);
    }

    [Fact]
    public void GetMailingLists_SupportsShowInactiveFiltering()
    {
        var source = ReadRepositoryFile(
            "src", "api", "EMA.Api", "Functions", "MailingLists", "MailingListsFunctions.cs");

        Assert.Contains("showInactive", source);
        Assert.Contains("ml.IsActive = @IsActive", source);
        Assert.Contains("@IsActive\", showInactive ? 0 : 1", source);
    }

    [Fact]
    public void UpdateMailingList_PersistsIsActiveAndAuditFields()
    {
        var source = ReadRepositoryFile(
            "src", "api", "EMA.Api", "Functions", "MailingLists", "MailingListsFunctions.cs");

        Assert.Contains("IsActive = @IsActive", source);
        Assert.Contains("DateTimeUpdated = GETDATE()", source);
        Assert.Contains("UpdatedByUserID = @UserID", source);
        Assert.DoesNotContain("DeleteMailingList", source);
    }

    [Fact]
    public void MailingListService_SupportsShowInactiveAndHasNoDeleteMethod()
    {
        var serviceSource = ReadRepositoryFile(
            "src", "app", "EMA.App", "Services", "MailingListService.cs");
        var interfaceSource = ReadRepositoryFile(
            "src", "app", "EMA.App", "Services", "IMailingListService.cs");

        Assert.Contains("showInactive", serviceSource);
        Assert.Contains("showInactive", interfaceSource);
        Assert.DoesNotContain("DeleteMailingList", serviceSource);
        Assert.DoesNotContain("DeleteMailingList", interfaceSource);
    }

    private string ReadRepositoryFile(params string[] pathParts) =>
        File.ReadAllText(Path.Combine(new[] { _repositoryRoot }.Concat(pathParts).ToArray()));

    private static int CountOccurrences(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "email-money-app.slnx")) ||
                Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
