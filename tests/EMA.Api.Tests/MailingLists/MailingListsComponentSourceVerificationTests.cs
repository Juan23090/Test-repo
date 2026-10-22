namespace EMA.Api.Tests.MailingLists;

public sealed class MailingListsComponentSourceVerificationTests
{
    private readonly string _repositoryRoot = FindRepositoryRoot();

    [Fact]
    public void HowMailingListsWork_ContainsRequiredRoutesAndPhilosophyContent()
    {
        var path = Path.Combine(_repositoryRoot, "src", "app", "EMA.App", "Components", "Pages", "Operations", "HowMailingListsWork.razor");
        var content = File.ReadAllText(path);

        Assert.Contains("@page \"/mailing-lists-how-it-works\"", content);
        Assert.Contains("/mailing-lists-index", content);
        Assert.Contains("How Donors Make the Send List", content);
        Assert.Contains("Opt In Means", content);
    }

    [Fact]
    public void MailingListsIndex_ContainsListingTableAndCreateButton()
    {
        var path = Path.Combine(_repositoryRoot, "src", "app", "EMA.App", "Components", "Pages", "Operations", "MailingListsIndex.razor");
        var content = File.ReadAllText(path);

        Assert.Contains("@page \"/mailing-lists-index\"", content);
        Assert.Contains("Create New Mailing List", content);
        Assert.Contains("Show Inactive", content);
        Assert.Contains("ToggleShowInactive", content);
        Assert.Contains("NavigateToCreate", content);
        Assert.Contains("/mailing-list-create", content);
        Assert.Contains("/mailing-list-details/", content);
    }

    [Fact]
    public void CreateMailingList_ContainsFullInputFormAndSaveHandler()
    {
        var path = Path.Combine(_repositoryRoot, "src", "app", "EMA.App", "Components", "Pages", "Operations", "CreateMailingList.razor");
        var content = File.ReadAllText(path);

        Assert.Contains("@page \"/mailing-list-create\"", content);
        Assert.Contains("Model.Name", content);
        Assert.Contains("Donation1StartDate", content);
        Assert.Contains("Donation1EndDate", content);
        Assert.Contains("Donation1MoneyFrom", content);
        Assert.Contains("Donation1MoneyTo", content);
        Assert.Contains("AndOr", content);
        Assert.Contains("CSV", content);
        Assert.Contains("SendMethodID", content);
        Assert.Contains("SaveMailingList", content);
    }

    [Fact]
    public void MailingListDetails_ContainsPrepareReviewSendAndEditActions()
    {
        var path = Path.Combine(_repositoryRoot, "src", "app", "EMA.App", "Components", "Pages", "Operations", "MailingListDetails.razor");
        var content = File.ReadAllText(path);

        Assert.Contains("@page \"/mailing-list-details/{ListID:int}\"", content);
        Assert.Contains("Edit Mailing List", content);
        Assert.Contains("EditModel.IsActive", content);
        Assert.Contains("isActiveCheckEdit", content);
        Assert.DoesNotContain("Delete List", content);
        Assert.DoesNotContain("ConfirmDelete", content);
        Assert.Contains("PrepareInstance", content);
        Assert.Contains("NavigateToReview", content);
        Assert.Contains("DownloadInstanceExport", content);
        Assert.Contains("SendExport", content);
    }

    [Fact]
    public void MailingListReviewDonors_ContainsToggleHandlersAndSearchFilter()
    {
        var path = Path.Combine(_repositoryRoot, "src", "app", "EMA.App", "Components", "Pages", "Operations", "MailingListReviewDonors.razor");
        var content = File.ReadAllText(path);

        Assert.Contains("@page \"/mailing-list-review/{ListID:int}\"", content);
        Assert.Contains("ToggleOptIn", content);
        Assert.Contains("ToggleOptOut", content);
        Assert.Contains("OnSearchInput", content);
        Assert.Contains("ListAllDonors", content);
    }

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
