namespace EMA.Api.Tests.Onboarding;

public sealed class OrganizationOnboardingPersistenceContractTests
{
    [Fact]
    public void RepositoryUsesTransactionAndTenantScopedReferenceValidation()
    {
        var source =
            File.ReadAllText(
                FindRepositoryFile(
                    "src",
                    "api",
                    "EMA.Api",
                    "Services",
                    "Onboarding",
                    "SqlOrganizationOnboardingRepository.cs"));

        Assert.Contains("BeginTransactionAsync", source);
        Assert.Contains("CommitAsync", source);
        Assert.Contains("RollbackAsync", source);
        Assert.Contains("EMAOrganizationID = @OrganizationID", source);
        Assert.Contains("dbo.Donor", source);
        Assert.Contains("dbo.OrganizationEvent", source);
        Assert.Contains("dbo.OrganizationDonationSource", source);
        Assert.Contains("dbo.OrganizationMethodOfGiving", source);
        Assert.Contains("dbo.OrganizationFund", source);
        Assert.Contains("dbo.OrganizationCampus", source);
    }

    [Fact]
    public void RepositoryReadsAndWritesAllTask22CreditDebitDefaults()
    {
        var source =
            File.ReadAllText(
                FindRepositoryFile(
                    "src",
                    "api",
                    "EMA.Api",
                    "Services",
                    "Onboarding",
                    "SqlOrganizationOnboardingRepository.cs"));

        Assert.Contains("EMAEventIDForCreditDebitCards", source);
        Assert.Contains("EMASourceIDForCreditDebitCards", source);
        Assert.Contains("EMAMethodOfGivingIDForCreditDebitCards", source);
        Assert.Contains("EMAFundIDForCreditDebitCards", source);
        Assert.Contains("EMACampusIDForCreditDebitCards", source);
    }

    [Fact]
    public void Task22DatabaseFileDoesNotModifyRelationshipsOrCascadeBehavior()
    {
        var source =
            File.ReadAllText(
                FindRepositoryFile(
                    "src",
                    "db",
                    "20260817_Task22DonationAttributeOnboarding.sql"));

        Assert.Contains("EMAEventIDForCreditDebitCards", source);
        Assert.Contains("MenuItemID = 19815", source);
        Assert.Contains("'/pads'", source);

        var executableText = source.ToUpperInvariant();

        Assert.DoesNotContain("ADD CONSTRAINT", executableText);
        Assert.DoesNotContain("DROP CONSTRAINT", executableText);
        Assert.DoesNotContain("ON UPDATE CASCADE", executableText);
        Assert.DoesNotContain("ON DELETE CASCADE", executableText);
        Assert.DoesNotContain("ALTER TABLE DBO.MENUITEMUSAGE", executableText);
    }

    [Fact]
    public void BasicInfoNoLongerOwnsDonationEmailField()
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
                    "BasicInfo.razor"));

        Assert.DoesNotContain("<strong>Donation Email:</strong>", source);
        Assert.DoesNotContain("missingFields.Add(\n                \"Donation Email\")", source);
        Assert.Contains("Task 22 moved Donation Email to the eTransfer setup page", source);
    }

    [Fact]
    public void UpdateOrganizationEnforcesEtransferEmailPrerequisitesServerSide()
    {
        var source =
            File.ReadAllText(
                FindRepositoryFile(
                    "src",
                    "api",
                    "EMA.Api",
                    "Functions",
                    "Organization",
                    "UpdateOrganization.cs"));

        Assert.Contains("HasEtransferSetupPrerequisitesAsync", source);
        Assert.Contains("EMAMethodOfGivingIDForETransfers IS NOT NULL", source);
        Assert.Contains("EMAFundIDForEtransfer IS NOT NULL", source);
        Assert.Contains("before setting the donation email", source);
    }


    [Fact]
    public void OnboardingSettingsReadOrganizationUseFlags()
    {
        var source =
            File.ReadAllText(
                FindRepositoryFile(
                    "src",
                    "api",
                    "EMA.Api",
                    "Services",
                    "Onboarding",
                    "SqlOrganizationOnboardingRepository.cs"));

        Assert.Contains("UseAnonymous", source);
        Assert.Contains("UseCampuses", source);
        Assert.Contains("UseEvents", source);
        Assert.Contains("UseFunds", source);
        Assert.Contains("UseMethodsOfGiving", source);
        Assert.Contains("UseSources", source);
        Assert.Contains("UseTributeTypes", source);
        Assert.Contains("GetUseFlag", source);
    }

    [Fact]
    public void AttributeSetupScreensConditionallyRenderRowsFromUseFlags()
    {
        var setupFiles =
            new[]
            {
                "EtransferSetup.razor",
                "PadSetup.razor",
                "CreditDebitSetup.razor"
            };

        foreach (var fileName in setupFiles)
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

            Assert.Contains("@if (Settings.UseEvents)", source);
            Assert.Contains("@if (Settings.UseSources)", source);
            Assert.Contains("@if (Settings.UseMethodsOfGiving)", source);
            Assert.Contains("@if (Settings.UseFunds)", source);
            Assert.Contains("@if (Settings.UseCampuses)", source);
            Assert.Contains("HasVisibleAttributeRows", source);
        }
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
