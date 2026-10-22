namespace EMA.Api.Tests.Organization;

public sealed class GetAllOrganizationsNumericReadTests
{
    [Fact]
    public void NumericOrganizationColumnsDoNotAssumeSqlInt32()
    {
        var source = File.ReadAllText(
            FindRepositoryFile(
                "src", "api", "EMA.Api", "Functions", "Organization",
                "GetAllOrganizations.cs"));

        foreach (var column in new[]
        {
            "EMAOrganizationID",
            "OrganizationStatusID",
            "ChMSID",
            "AccountingSoftwareID"
        })
        {
            Assert.DoesNotContain(
                $"reader.GetInt32(reader.GetOrdinal(\"{column}\"))",
                source);

            Assert.Contains(
                $"Convert.ToInt32(reader.GetValue(reader.GetOrdinal(\"{column}\")))",
                source);
        }
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(
                new[] { directory.FullName }.Concat(segments).ToArray());

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
