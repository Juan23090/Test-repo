using Xunit;

namespace EMA.App.Tests.Onboarding;

public sealed class Task22OnboardingSourceTests
{
    [Theory]
    [InlineData("AnonymousDonorSetup.razor", "@page \"/anonymous\"")]
    [InlineData("DepositSetup.razor", "@page \"/deposit\"")]
    [InlineData("EtransferSetup.razor", "@page \"/etransfers\"")]
    [InlineData("PadSetup.razor", "@page \"/pads\"")]
    [InlineData("CreditDebitSetup.razor", "@page \"/crdb\"")]
    public void Task22PagesUseExpectedRoutes(
        string fileName,
        string route)
    {
        var source =
            File.ReadAllText(
                FindRepositoryFile(
                    "src",
                    "app",
                    "EMA.App",
                    "Components",
                    "Pages",
                    "Operations",
                    fileName));

        Assert.Contains(route, source);
    }

    [Fact]
    public void DepositSlipRouteKeepsExistingReportAndHostsTask22Setup()
    {
        var reportSource =
            File.ReadAllText(
                FindRepositoryFile(
                    "src",
                    "app",
                    "EMA.App",
                    "Components",
                    "Pages",
                    "Operations",
                    "Deposits.razor"));

        var setupSource =
            File.ReadAllText(
                FindRepositoryFile(
                    "src",
                    "app",
                    "EMA.App",
                    "Components",
                    "Pages",
                    "Operations",
                    "DepositSetup.razor"));

        Assert.Contains("@page \"/deposit-slip\"", reportSource);
        Assert.Contains("<DepositSetup />", reportSource);
        Assert.DoesNotContain("@page \"/deposit-slip\"", setupSource);
        Assert.Contains("@page \"/deposit\"", setupSource);
    }

    [Fact]
    public void EtransferScreenRequiresMethodAndFundBeforeDonationEmail()
    {
        var source =
            File.ReadAllText(
                FindRepositoryFile(
                    "src",
                    "app",
                    "EMA.App",
                    "Components",
                    "Pages",
                    "Operations",
                    "EtransferSetup.razor"));

        Assert.Contains("EMAMethodOfGivingIDForETransfers is > 0", source);
        Assert.Contains("EMAFundIDForEtransfer is > 0", source);
        Assert.Contains("disabled=\"@(!HasEmailPrerequisites)\"", source);
        Assert.Contains("CheckEtransferAliasAvailabilityAsync", source);
        Assert.Contains("UpdateOrganizationWithResultAsync", source);
    }

    [Fact]
    public void AnonymousScreenLinksToAuthorizedNestedDonorMaintenanceAndSupportsSelection()
    {
        var source =
            File.ReadAllText(
                FindRepositoryFile(
                    "src",
                    "app",
                    "EMA.App",
                    "Components",
                    "Pages",
                    "Operations",
                    "AnonymousDonorSetup.razor"));

        Assert.Contains("Click Here to Setup the Anonymous Donor", source);
        Assert.Contains("Then Come Back Here to Select It Below", source);
        Assert.Contains("/donor-maintenance?mode=anonymous-setup", source);
        Assert.Contains("DonorService.SearchAsync", source);
        Assert.Contains("EMADonorIDForAnonymous", source);
    }

    [Fact]
    public void DonorMaintenanceUsesOneCanonicalRoute()
    {
        var source =
            File.ReadAllText(
                FindRepositoryFile(
                    "src",
                    "app",
                    "EMA.App",
                    "Components",
                    "Pages",
                    "Operations",
                    "Donors.razor"));

        Assert.Contains("@page \"/donor-maintenance\"", source);
        Assert.DoesNotContain("@page \"/anonymous/setup-donor\"", source);
    }
    [Fact]
    public void DepositScreenIncludesAllRequestedFields()
    {
        var source =
            File.ReadAllText(
                FindRepositoryFile(
                    "src",
                    "app",
                    "EMA.App",
                    "Components",
                    "Pages",
                    "Operations",
                    "DepositSetup.razor"));

        Assert.Contains("BankBranchNumber", source);
        Assert.Contains("BankAccountNumber", source);
        Assert.Contains("EMAMethodOfGivingIDForCheques", source);
        Assert.Contains("EMAMethodOfGivingIDForCash", source);
    }

    [Theory]
    [InlineData("PadSetup.razor", "EMAEventIDForPADs", "EMASourceIDForPADs", "EMAMethodOfGivingIDForPADs", "EMAFundIDForPADs", "EMACampusIDForPADs")]
    [InlineData("CreditDebitSetup.razor", "EMAEventIDForCreditDebitCards", "EMASourceIDForCreditDebitCards", "EMAMethodOfGivingIDForCreditDebitCards", "EMAFundIDForCreditDebitCards", "EMACampusIDForCreditDebitCards")]
    public void DonationChannelScreensIncludeAllFiveDefaults(
        string fileName,
        string eventField,
        string sourceField,
        string methodField,
        string fundField,
        string campusField)
    {
        var source =
            File.ReadAllText(
                FindRepositoryFile(
                    "src",
                    "app",
                    "EMA.App",
                    "Components",
                    "Pages",
                    "Operations",
                    fileName));

        Assert.Contains(eventField, source);
        Assert.Contains(sourceField, source);
        Assert.Contains(methodField, source);
        Assert.Contains(fundField, source);
        Assert.Contains(campusField, source);
    }

    [Fact]
    public void OrganizationOnboardingServiceIsRegistered()
    {
        var source =
            File.ReadAllText(
                FindRepositoryFile(
                    "src",
                    "app",
                    "EMA.App",
                    "Program.cs"));

        Assert.Contains("IOrganizationOnboardingService", source);
        Assert.Contains("OrganizationOnboardingService", source);
    }

    private static string FindRepositoryFile(
        params string[] segments)
    {
        var directory =
            new DirectoryInfo(
                AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate =
                Path.Combine(
                    new[] { directory.FullName }
                        .Concat(segments)
                        .ToArray());

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the EMA repository root.");
    }
}
