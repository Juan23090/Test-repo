namespace EMA.Api.Tests.Security;

public sealed class UnifiedLoginSchemaContractTests
{
    [Fact]
    public void DonorLookupUsesTheOrganizationsConfiguredDonorRole()
    {
        var source =
            ReadRepositoryFile(
                "src",
                "api",
                "EMA.Api",
                "Services",
                "Authentication",
                "UnifiedLoginCandidateQueries.cs");

        Assert.Contains(
            "organization.DonorRoleID",
            source);

        Assert.Contains(
            "donorRole.RoleID",
            source);
    }

    [Fact]
    public void SourceDoesNotReferenceTheUnprovisionedDonorRoleFlag()
    {
        var repositoryRoot =
            FindRepositoryRoot();

        var offenders =
            Directory
                .EnumerateFiles(
                    Path.Combine(
                        repositoryRoot,
                        "src"),
                    "*",
                    SearchOption.AllDirectories)
                .Where(path =>
                    Path.GetExtension(path) is
                        ".cs" or ".sql")
                .Where(path =>
                    File.ReadAllText(path)
                        .Contains(
                            "IsADonorRole",
                            StringComparison.Ordinal))
                .Select(path =>
                    Path.GetRelativePath(
                        repositoryRoot,
                        path))
                .OrderBy(path =>
                    path,
                    StringComparer.Ordinal)
                .ToList();

        Assert.True(
            offenders.Count == 0,
            "Source files still reference the unprovisioned " +
            $"Role.IsADonorRole field: {string.Join(", ", offenders)}");
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
