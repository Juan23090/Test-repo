using Xunit;

namespace EMA.Api.Tests.Onboarding;

public sealed class PublicOrganizationPersistenceContractTests
{
    [Fact]
    public void PublicOrganizationPersistenceCreatesOrganizationAndDefersTriggerOwnedWrites()
    {
        var source = ReadApiSource(
            "Services", "Onboarding", "SqlPublicOrganizationRequestRepository.cs");

        Assert.Contains("BeginTransactionAsync", source);
        Assert.Contains("OUTPUT INSERTED.EMAOrganizationID", source);
        Assert.Contains("INTO @InsertedOrganization (EMAOrganizationID)", source);
        Assert.Contains("SELECT TOP (1) EMAOrganizationID", source);
        Assert.Contains("TR_Organization_AfterInsert", source);
        Assert.Contains("creates/reuses the admin User", source);
        Assert.Contains("creates the initial OrganizationJourney record", source);
        Assert.Contains("SystemSettings", source);
        Assert.Contains("UserIDForFollowups", source);
        Assert.Contains("ORDER BY SettingID DESC", source);
        Assert.DoesNotContain("GetCreatedOrganizationUserIDAsync", source);
        Assert.DoesNotContain("FROM dbo.OrganizationUser", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO dbo.OrganizationJourney", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CommitAsync", source);
        Assert.Contains("RollbackAsync", source);
    }

    [Fact]
    public void PublicOrganizationPersistenceValidatesReferenceDataBeforeInsert()
    {
        var source = ReadApiSource(
            "Services", "Onboarding", "SqlPublicOrganizationRequestRepository.cs");

        Assert.Contains("ValidateReferenceDataAsync", source);
        Assert.Contains("dbo.ChMS", source);
        Assert.Contains("dbo.AccountingSoftware", source);
        Assert.Contains("dbo.OrganizationStatus", source);
        Assert.Contains("PublicOrganizationRequestValidator.IsAllowedStatus", source);
    }

    [Fact]
    public void PublicOrganizationPersistenceDoesNotCreateOrModifySandboxData()
    {
        var source = ReadApiSource(
            "Services", "Onboarding", "SqlPublicOrganizationRequestRepository.cs");

        Assert.DoesNotContain("sp_Sandbox", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SandboxFunctions", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SandboxOrganization", source, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadApiSource(params string[] segments)
    {
        var path = segments.Aggregate(
            Path.Combine(FindRepositoryRoot(), "src", "api", "EMA.Api"),
            Path.Combine);

        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "api", "EMA.Api")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the EMA repository root.");
    }
}
