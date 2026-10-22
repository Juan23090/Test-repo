namespace EMA.Api.Tests.Security;

/// <summary>
/// The Tribute Donors endpoints expose donor PII (name, email, phone, street address, postal code)
/// and can email it as a PDF attachment. The endpoint policy registry only establishes that the
/// caller is an authenticated organization user, which would let any user in the organization read
/// the report even when their role does not grant the Tribute Report screen.
///
/// These tests pin the server-side permission check so it cannot be dropped later.
/// </summary>
public sealed class TributeDonorsSecurityContractTests
{
    [Theory]
    [InlineData("GetTributeDonorsReport.cs")]
    [InlineData("GetTributeDonorsReportPdf.cs")]
    [InlineData("PostTributeDonorsReportEmail.cs")]
    public void TributeDonorEndpointsRequireTheTributeReportMenuPermission(
        string fileName)
    {
        var source =
            ReadRepositoryFile(
                "src",
                "api",
                "EMA.Api",
                "Functions",
                "Reports",
                fileName);

        Assert.Contains(
            "CanAccessMenuItemUrlAsync",
            source);

        Assert.Contains(
            "TributeDonorsReport.MenuItemUrl",
            source);

        Assert.Contains(
            "HttpStatusCode.Forbidden",
            source);

        // The identity must come from the validated token, never from the route or body.
        Assert.Contains(
            "req.GetRequiredApiIdentity()",
            source);
    }

    [Fact]
    public void TheTributeReportMenuUrlIsResolvedByUrlRatherThanAHardCodedMenuItemID()
    {
        var source =
            ReadRepositoryFile(
                "src",
                "api",
                "EMA.Api",
                "Functions",
                "Reports",
                "TributeDonorsReport.cs");

        Assert.Contains(
            "MenuItemUrl = \"/tribute-report\"",
            source);
    }

    [Fact]
    public void MenuItemAccessIsCheckedAgainstCurrentDatabaseRolesNotTokenClaims()
    {
        var source =
            ReadRepositoryFile(
                "src",
                "api",
                "EMA.Api",
                "Services",
                "Security",
                "MenuItemAccessService.cs");

        // Roles are re-read from the database so revoking a role takes effect immediately
        // rather than when the caller's session expires.
        Assert.Contains(
            "dbo.OrganizationUserRole",
            source);

        Assert.Contains(
            "dbo.RoleMenuItem",
            source);

        // The organization boundary stays in place alongside the permission check.
        Assert.Contains(
            "ou.EMAOrganizationID = @OrgID",
            source);

        // Donor sessions must never satisfy an organization-user screen permission.
        Assert.Contains(
            "ApiActorType.OrganizationUser",
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
