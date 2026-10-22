namespace EMA.Api.Tests.Navigation;

public sealed class Task44DuplicateSettingsHeaderTests
{
    [Fact]
    public void InsiderMenuDoesNotInjectOrganizationWizardAsHeader()
    {
        var source =
            File.ReadAllText(
                FindRepositoryFile(
                    "src",
                    "api",
                    "EMA.Api",
                    "Functions",
                    "Roles",
                    "GetMenuItemsByUser.cs"));

        Assert.Contains(
            "mi.MenuItemCategoryID =",
            source);
        Assert.Contains(
            "@CategoryID",
            source);
        Assert.DoesNotContain(
            "LoadSharedSetupWizardItemAsync",
            source);
        Assert.DoesNotContain(
            "@OrganizationCategoryID",
            source);
        Assert.DoesNotContain(
            "sharedWizardItem.MenuItemCategoryID",
            source);
        Assert.DoesNotContain(
            "sharedWizardItem.Row = 1",
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
