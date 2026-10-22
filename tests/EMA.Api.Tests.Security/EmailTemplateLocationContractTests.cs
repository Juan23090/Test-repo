namespace EMA.Api.Tests.Security;

public sealed class EmailTemplateLocationContractTests
{
    private static readonly string[] RequiredTemplateFiles =
    {
        "DonationAcknowledgement.html",
        "DonationStepNotice.html",
        "NonInteracEmail.html",
        "LoginVerification.html",
        "EmailAddressVerification.html",
        "DonorRegistrationVerification.html"
    };

    [Fact]
    public void AllApiEmailTemplatesAreStoredInTheCentralTemplateDirectory()
    {
        var repositoryRoot =
            FindRepositoryRoot();

        var templateDirectory =
            Path.Combine(
                repositoryRoot,
                "src",
                "api",
                "EmailTemplates");

        Assert.True(
            Directory.Exists(
                templateDirectory));

        foreach (var templateFile in
                 RequiredTemplateFiles)
        {
            Assert.True(
                File.Exists(
                    Path.Combine(
                        templateDirectory,
                        templateFile)),
                $"Missing centralized email template: {templateFile}");
        }

        Assert.False(
            Directory.Exists(
                Path.Combine(
                    repositoryRoot,
                    "src",
                    "api",
                    "EMA.Api",
                    "EmailTemplates")));
    }

    [Fact]
    public void ApiBuildPackagesTheCentralizedTemplates()
    {
        var project =
            ReadRepositoryFile(
                "src",
                "api",
                "EMA.Api",
                "EMA_Api.csproj");

        Assert.Contains(
            @"..\EmailTemplates\**\*.html",
            project);

        Assert.Contains(
            @"<TargetPath>EmailTemplates\%(RecursiveDir)%(Filename)%(Extension)</TargetPath>",
            project);
    }

    [Fact]
    public void VerificationEmailsUseCentralizedTemplateFiles()
    {
        var sender =
            ReadRepositoryFile(
                "src",
                "api",
                "EMA.Api",
                "Services",
                "Authentication",
                "AcsTwoFactorEmailSender.cs");

        Assert.Contains(
            "\"LoginVerification.html\"",
            sender);

        Assert.Contains(
            "\"EmailAddressVerification.html\"",
            sender);

        Assert.Contains(
            "\"DonorRegistrationVerification.html\"",
            sender);

        Assert.DoesNotContain(
            "<!doctype html>",
            sender,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VerificationEmailsAreWrittenToEmailSendLogWithoutTheCode()
    {
        var sender =
            ReadRepositoryFile(
                "src",
                "api",
                "EMA.Api",
                "Services",
                "Authentication",
                "AcsTwoFactorEmailSender.cs");

        Assert.Contains(
            "INSERT INTO dbo.EmailSendLog",
            sender);

        Assert.Contains(
            "\"Authentication\"",
            sender);

        Assert.Contains(
            "ProviderOperationID",
            sender);

        Assert.DoesNotContain(
            "CodeHash",
            sender);

        Assert.DoesNotContain(
            "@Code",
            sender);
    }

    [Fact]
    public void SystemEmailLogMigrationAllowsNullDonationContext()
    {
        var migration =
            ReadRepositoryFile(
                "src",
                "db",
                "migrations",
                "20260908_AllowSystemEmailSendLogEntries.sql");

        Assert.Contains(
            "ALTER COLUMN EMADonationID BIGINT NULL",
            migration);

        Assert.Contains(
            "ALTER COLUMN DonationStepID INT NULL",
            migration);

        Assert.Contains(
            "CK_EmailSendLog_DonationContext",
            migration);
    }

    private static string ReadRepositoryFile(
        params string[] segments)
    {
        var path =
            segments.Aggregate(
                FindRepositoryRoot(),
                Path.Combine);

        return File.ReadAllText(
            path);
    }

    private static string FindRepositoryRoot()
    {
        var directory =
            new DirectoryInfo(
                AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(
                    Path.Combine(
                        directory.FullName,
                        "src",
                        "api",
                        "EMA.Api")))
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
