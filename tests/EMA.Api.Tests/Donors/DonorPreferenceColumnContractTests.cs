namespace EMA.Api.Tests.Donors;

public sealed class DonorPreferenceColumnContractTests
{
    private static readonly string[] CanonicalColumns =
    {
        "IsDonorPilotingEtransfers",
        "AcknowledgmentForManualTransactions",
        "AcknowledgementForAutomatedTransactions"
    };

    private static readonly string[] DuplicateColumns =
    {
        "PilotEtransferIngestion",
        "ReceiveAcknowledgementsThatArentEtransfers",
        "AcknowledgmentsThatArentEtransfers"
    };

    [Theory]
    [InlineData("src/api/EMA.Api/Functions/Donors/CreateDonor.cs")]
    [InlineData("src/api/EMA.Api/Functions/Donors/UpdateDonor.cs")]
    [InlineData("src/api/EMA.Api/Functions/Donors/GetDonorById.cs")]
    [InlineData("src/api/EMA.Api/Functions/Donors/GetDonors.cs")]
    [InlineData("src/app/EMA.App/Models/DonorItem.cs")]
    [InlineData("src/app/EMA.App/Services/DonorService.cs")]
    [InlineData("src/app/EMA.App/Components/Pages/Operations/Donors.razor")]
    [InlineData("src/db/sp_CreateDonor.sql")]
    [InlineData("src/db/sp_UpdateDonor.sql")]
    [InlineData("src/db/20260819_CreateEmaDemoOrganization.sql")]
    public void DonorPreferencePathsUseOnlyCanonicalColumns(
        string relativePath)
    {
        var source =
            File.ReadAllText(
                FindRepositoryFile(
                    relativePath.Split('/')));

        foreach (var canonicalColumn in CanonicalColumns)
        {
            Assert.Contains(
                canonicalColumn,
                source);
        }

        foreach (var duplicateColumn in DuplicateColumns)
        {
            Assert.DoesNotContain(
                duplicateColumn,
                source);
        }
    }

    [Fact]
    public void CleanupMigrationRequiresCanonicalProceduresBeforeColumnDrop()
    {
        var source =
            File.ReadAllText(
                FindRepositoryFile(
                    "src",
                    "db",
                    "20260828_RemoveDuplicateDonorPreferenceColumns.sql"));

        Assert.Contains(
            "Apply the canonical dbo.sp_CreateDonor definition",
            source);
        Assert.Contains(
            "Apply the canonical dbo.sp_UpdateDonor definition",
            source);
        Assert.Contains(
            "OBJECT_DEFINITION",
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
