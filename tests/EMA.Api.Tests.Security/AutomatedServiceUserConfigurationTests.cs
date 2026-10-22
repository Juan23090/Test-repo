namespace EMA.Api.Tests.Security;

public sealed class AutomatedServiceUserConfigurationTests
{
    private const string SharedSetting = "AUTOMATED_SERVICE_USER_ID";

    [Theory]
    [InlineData("src", "api", "EMA.Api", "Functions", "Authentication", "VerifyDonorSelfRegistration.cs")]
    [InlineData("src", "api", "EMA.Api", "Services", "EmailIngestion", "DonationEmailIngestionService.cs")]
    [InlineData("src", "api", "EMA.Api", "Services", "Sandbox", "SandboxService.cs")]
    public void AutomatedServicesUseTheSharedAuditActorSetting(
        params string[] pathSegments)
    {
        var source =
            File.ReadAllText(
                FindRepositoryFile(
                    pathSegments));

        Assert.Contains(
            SharedSetting,
            source);
    }

    [Fact]
    public void LegacyAutomatedServiceUserSettingsAreRemoved()
    {
        var sourceRoot =
            FindRepositoryDirectory(
                "src",
                "api",
                "EMA.Api");

        var source =
            string.Join(
                '\n',
                Directory.EnumerateFiles(
                        sourceRoot,
                        "*.cs",
                        SearchOption.AllDirectories)
                    .Select(
                        File.ReadAllText));

        Assert.DoesNotContain(
            "DONOR_SELF_REGISTRATION_USER_ID",
            source);
        Assert.DoesNotContain(
            "EMAIL_INGESTION_USER_ID",
            source);
        Assert.DoesNotContain(
            "SandboxCreatedByUserID",
            source);
    }

    private static string FindRepositoryFile(
        params string[] segments) =>
        Path.Combine(
            FindRepositoryRoot(),
            Path.Combine(
                segments));

    private static string FindRepositoryDirectory(
        params string[] segments) =>
        Path.Combine(
            FindRepositoryRoot(),
            Path.Combine(
                segments));

    private static string FindRepositoryRoot()
    {
        var directory =
            new DirectoryInfo(
                AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(
                    Path.Combine(
                        directory.FullName,
                        "email-money-app.slnx")))
            {
                return directory.FullName;
            }

            directory =
                directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the EMA repository root.");
    }
}
