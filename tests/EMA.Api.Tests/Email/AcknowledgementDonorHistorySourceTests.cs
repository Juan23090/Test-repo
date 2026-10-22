namespace EMA.Api.Tests.Email;

public sealed class AcknowledgementDonorHistorySourceTests
{
    [Fact]
    public void AcknowledgementLinksToSecureDonorHistory()
    {
        var repositoryRoot =
            FindRepositoryRoot();

        var template =
            ReadSource(
                repositoryRoot,
                "src",
                "api",
                "EmailTemplates",
                "DonationAcknowledgement.html");

        var emailTemplateService =
            ReadSource(
                repositoryRoot,
                "src",
                "api",
                "EMA.Api",
                "Services",
                "Email",
                "EmailTemplateService.cs");

        var tokenService =
            ReadSource(
                repositoryRoot,
                "src",
                "api",
                "EMA.Api",
                "Services",
                "Authentication",
                "DonorAccessTokenService.cs");

        var donorHistoryPage =
            ReadSource(
                repositoryRoot,
                "src",
                "app",
                "EMA.App",
                "Components",
                "Pages",
                "Donor",
                "DonorHistory.razor");

        var donorHistoryEndpoint =
            ReadSource(
                repositoryRoot,
                "src",
                "api",
                "EMA.Api",
                "Functions",
                "Donations",
                "GetDonationHistory.cs");

        Assert.Contains(
            "href=\"{{GivingHistoryUrl}}\"",
            template);

        Assert.Contains(
            "EMA Giving History",
            template);

        Assert.Contains(
            ".CreateGivingHistoryUrlAsync(",
            emailTemplateService);

        Assert.Contains(
            "/giving-history#token=",
            tokenService);

        Assert.Contains(
            "@page \"/giving-history\"",
            donorHistoryPage);

        Assert.Contains(
            "GetFragmentAccessToken",
            donorHistoryPage);

        Assert.Contains(
            "new AuthenticationHeaderValue(",
            donorHistoryPage);

        Assert.Contains(
            "\"Bearer\"",
            donorHistoryPage);

        Assert.Contains(
            "api/donor-history?year={year}",
            donorHistoryPage);

        Assert.Contains(
            ".TryValidateGivingHistoryToken(",
            donorHistoryEndpoint);

        Assert.DoesNotContain(
            "DonorId:int",
            donorHistoryPage);

        Assert.False(
            File.Exists(
                Path.Combine(
                    repositoryRoot,
                    "src",
                    "app",
                    "EMA.App",
                    "Components",
                    "Pages",
                    "Unused",
                    "DonorHistory.razor")));
    }

    private static string ReadSource(
        string repositoryRoot,
        params string[] pathParts)
    {
        return File.ReadAllText(
            Path.Combine(
                new[]
                {
                    repositoryRoot
                }
                .Concat(
                    pathParts)
                .ToArray()));
    }

    private static string FindRepositoryRoot()
    {
        var directory =
            new DirectoryInfo(
                AppContext.BaseDirectory);

        while (directory is not null)
        {
            var marker =
                Path.Combine(
                    directory.FullName,
                    "src",
                    "app",
                    "EMA.App");

            if (Directory.Exists(
                    marker))
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
