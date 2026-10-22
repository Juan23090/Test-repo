namespace EMA.Api.Tests.Security;

public sealed class InsiderRoleBoundaryContractTests
{
    [Fact]
    public void RoleDiscoveryRestrictsTheInsiderRoleToThePlatformOrganization()
    {
        var source =
            ReadRepositoryFile(
                "src",
                "api",
                "EMA.Api",
                "Functions",
                "Roles",
                "GetRoles.cs");

        Assert.Contains(
            "role.RoleID <> @InsiderRoleID",
            source);

        Assert.Contains(
            "@EMAOrganizationID =",
            source);

        Assert.Matches(
            @"_authorization\s*\.PlatformOrganizationID",
            source);
    }

    [Fact]
    public void UserRoleSavingRestrictsTheInsiderRoleToThePlatformOrganization()
    {
        var source =
            ReadRepositoryFile(
                "src",
                "api",
                "EMA.Api",
                "Functions",
                "Users",
                "SaveUser.cs");

        Assert.Contains(
            "private const int InsiderRoleID = 9000;",
            source);

        Assert.Contains(
            "role.RoleID <> @InsiderRoleID",
            source);

        Assert.Contains(
            "@PlatformOrganizationID",
            source);
    }

    [Fact]
    public void UserCreationLetsSqlGenerateTheOrganizationUserIdentity()
    {
        var source =
            ReadRepositoryFile(
                "src",
                "api",
                "EMA.Api",
                "Functions",
                "Users",
                "SaveUser.cs");

        Assert.Contains(
            "INSERT INTO dbo.OrganizationUser",
            source);

        Assert.DoesNotContain(
            "dbo.OrganizationUserIDSequence",
            source);
    }

    [Fact]
    public void DatabaseTriggerRejectsInsiderRolesOutsideOrganizationOne()
    {
        var source =
            ReadRepositoryFile(
                "src",
                "db",
                "migrations",
                "20260904_RestrictInsiderRolesToPlatformOrganization.sql");

        Assert.Contains(
            "newAssignment.EMAOrganizationID <> 1",
            source);

        Assert.Contains(
            "newAssignment.RoleID = 9000",
            source);

        Assert.Contains(
            "roleGroupRole.RoleGroupID = 90",
            source);

        Assert.Contains(
            "AFTER INSERT, UPDATE",
            source);
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
