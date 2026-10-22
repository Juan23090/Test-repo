namespace EMA.Api.Tests.Security;

public sealed class DonorDashboardSecurityContractTests
{
    [Theory]
    [InlineData("GetDonationsByMonth.cs")]
    [InlineData("GetDonationsByFund.cs")]
    [InlineData("GetDonationsByMethod.cs")]
    public void DonorChartsDeriveDonorScopeFromTheValidatedIdentity(
        string fileName)
    {
        var source =
            ReadRepositoryFile(
                "src",
                "api",
                "EMA.Api",
                "Functions",
                "Dashboard",
                fileName);

        Assert.Contains(
            "identity.DonorID",
            source);

        Assert.Contains(
            "identity.ActorType",
            source);

        Assert.DoesNotContain(
            "req.Query[\"donorId\"]",
            source);
    }

    [Fact]
    public void DonorDashboardDoesNotSendADonorIDInTheQueryString()
    {
        var source =
            ReadRepositoryFile(
                "src",
                "app",
                "EMA.App",
                "Components",
                "Pages",
                "Donor",
                "DonorDashboard.razor");

        Assert.Contains(
            "/api/dashboard/donor-settings/{_orgID}",
            source);

        Assert.DoesNotContain(
            "donorId=",
            source);

        Assert.DoesNotContain(
            "/api/dashboard/kpis/",
            source);

        Assert.DoesNotContain(
            "/api/customization/settings/",
            source);
    }

    [Fact]
    public void DonorSettingsEndpointReturnsOnlyDashboardPreferences()
    {
        var source =
            ReadRepositoryFile(
                "src",
                "api",
                "EMA.Api",
                "Functions",
                "Dashboard",
                "GetDonorDashboardSettings.cs");

        Assert.Contains(
            "KPIDefaultHistoryDays",
            source);

        Assert.Contains(
            "KPIGraphDonorLandingPage",
            source);

        Assert.Contains(
            "identity.EMAOrganizationID",
            source);

        Assert.DoesNotContain(
            "AdminEmail",
            source);

        Assert.DoesNotContain(
            "BankAccountNumber",
            source);

        Assert.DoesNotContain(
            "EtransferEmailAddress",
            source);
    }

    [Fact]
    public void MainLayoutDoesNotLoadTheFullOrganizationForDonorSessions()
    {
        var source =
            ReadRepositoryFile(
                "src",
                "app",
                "EMA.App",
                "Layout",
                "MainLayout.razor");

        Assert.Contains(
            "if (session.IsDonorSession)",
            source);

        Assert.Contains(
            "OrganizationState.Clear();",
            source);

        var donorBranchStart =
            source.IndexOf(
                "if (session.IsDonorSession)",
                StringComparison.Ordinal);

        var organizationLoadStart =
            source.IndexOf(
                "await RestoreOrganizationAsync",
                donorBranchStart,
                StringComparison.Ordinal);

        var elseStart =
            source.IndexOf(
                "else",
                donorBranchStart,
                StringComparison.Ordinal);

        Assert.True(
            elseStart >= 0 &&
            organizationLoadStart > elseStart);
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
