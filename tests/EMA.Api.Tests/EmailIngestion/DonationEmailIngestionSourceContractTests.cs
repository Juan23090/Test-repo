namespace EMA.Api.Tests.EmailIngestion;

public sealed class DonationEmailIngestionSourceContractTests
{
    [Fact]
    public void StoredProcedureUsesExistingOrganizationDefaultFundDonationStep()
    {
        var source =
            File.ReadAllText(
                FindRepositoryFile(
                    "src",
                    "db",
                    "sp_IngestDonationEmail.sql"));

        Assert.Contains(
            "DECLARE @Step_DefaultFundApplied INT = 88;",
            source);

        Assert.DoesNotContain(
            "DECLARE @Step_DefaultFundApplied INT = 85;",
            source);
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
