using EMA.Api.Services.Security;

namespace EMA.Api.Tests.Security;

public sealed class ApiEndpointPolicyRegistryTests
{
    [Theory]
    [InlineData("UnifiedLoginLookup")]
    [InlineData("VerifyUnifiedLoginCode")]
    [InlineData("CompleteUnifiedLogin")]
    [InlineData("GetPublicRouteAccess")]
    [InlineData("GetPublicBrochureMenuItems")]
    [InlineData("GetPublicFaqs")]
    [InlineData("GetPublishedPublicFaqPage")]
    [InlineData("CreateSandbox")]
    [InlineData("GetPublicOnboardingLookups")]
    [InlineData("StartPublicOrganizationEmailVerification")]
    [InlineData("VerifyPublicOrganizationEmail")]
    [InlineData("SubmitPublicOrganizationRequest")]
    public void DeliberateAnonymousEndpointsArePublic(
        string functionName)
    {
        var policy =
            new ApiEndpointPolicyRegistry()
                .GetPolicy(
                    functionName);

        Assert.Equal(
            ApiEndpointAccess.Public,
            policy.Access);
    }

    [Theory]
    [InlineData("DonorOrganizationLoginLookup")]
    [InlineData("LookupDonorLogin")]
    [InlineData("LookupLoginUser")]
    [InlineData("StartDonorLoginVerification")]
    [InlineData("StartOrganizationLoginVerification")]
    [InlineData("TestEmail")]
    [InlineData("ResumeSandboxByPhone")]
    [InlineData("VerifyDonorLoginCode")]
    [InlineData("VerifyOrganizationLoginCode")]
    public void UnsafeOrUnimplementedEndpointsAreDisabled(
        string functionName)
    {
        var policy =
            new ApiEndpointPolicyRegistry()
                .GetPolicy(
                    functionName);

        Assert.Equal(
            ApiEndpointAccess.Disabled,
            policy.Access);
    }

    [Theory]
    [InlineData("CreateDonation")]
    [InlineData("UpdateDonation")]
    [InlineData("GetScreenActionSummary")]
    [InlineData("CreateScreenFaq")]
    [InlineData("CreateScreenEnhancementRequest")]
    [InlineData("CreateScreenSupportTicket")]
    [InlineData("SaveScreenEnhancementVote")]
    [InlineData("GetInsiderFaqs")]
    [InlineData("CreateInsiderFaq")]
    [InlineData("PublishPublicFaqPage")]
    [InlineData("UpdateInsiderFaq")]
    [InlineData("GetInsiderEnhancements")]
    [InlineData("UpdateInsiderEnhancement")]
    [InlineData("GetInsiderSupportTickets")]
    [InlineData("UpdateInsiderSupportTicket")]
    public void OrganizationUserEndpointsRequireAnOrganizationUser(
        string functionName)
    {
        var policy =
            new ApiEndpointPolicyRegistry()
                .GetPolicy(
                    functionName);

        Assert.Equal(
            ApiEndpointAccess.OrganizationUser,
            policy.Access);
    }

    [Theory]
    [InlineData("GetDonorHistory")]
    [InlineData("GetDonorReceipts")]
    [InlineData("GetDonorDirectedChanges")]
    [InlineData("SaveDonorDirectedChanges")]
    public void DonorLinkEndpointsValidateTheirOwnScopedTokens(
        string functionName)
    {
        var policy =
            new ApiEndpointPolicyRegistry()
                .GetPolicy(
                    functionName);

        Assert.Equal(
            ApiEndpointAccess.SelfContainedDonorToken,
            policy.Access);
    }

    [Fact]
    public void UnknownFunctionFailsClosedAsOrganizationUser()
    {
        var policy =
            new ApiEndpointPolicyRegistry()
                .GetPolicy(
                    "FutureFunction");

        Assert.Equal(
            ApiEndpointAccess.OrganizationUser,
            policy.Access);
    }

    [Theory]
    [InlineData(
        "CreateOrganization",
        ApiEndpointAccess.PlatformAdministrator)]
    [InlineData(
        "GetMenuItemResourceTypes",
        ApiEndpointAccess.PlatformAdministrator)]
    [InlineData(
        "SaveMenuItemResources",
        ApiEndpointAccess.PlatformAdministrator)]
    [InlineData(
        "GenerateMenuItemAudio",
        ApiEndpointAccess.PlatformAdministrator)]
    [InlineData(
        "GetSetupWizardForInsider",
        ApiEndpointAccess.PlatformAdministrator)]
    [InlineData(
        "GetBankLookups",
        ApiEndpointAccess.PlatformAdministrator)]
    [InlineData(
        "PostBank",
        ApiEndpointAccess.PlatformAdministrator)]
    [InlineData(
        "PutBank",
        ApiEndpointAccess.PlatformAdministrator)]
    [InlineData(
        "PostBankTransactionType",
        ApiEndpointAccess.PlatformAdministrator)]
    [InlineData(
        "PutBankTransactionType",
        ApiEndpointAccess.PlatformAdministrator)]
    [InlineData(
        "PostBankKeyPhrase",
        ApiEndpointAccess.PlatformAdministrator)]
    [InlineData(
        "PutBankKeyPhrase",
        ApiEndpointAccess.PlatformAdministrator)]
    [InlineData(
        "GetSetupWizard",
        ApiEndpointAccess.OrganizationUser)]
    [InlineData(
        "SaveUser",
        ApiEndpointAccess.OrganizationAdministrator)]
    [InlineData(
        "GetOrganizationOnboardingSettings",
        ApiEndpointAccess.OrganizationAdministrator)]
    [InlineData(
        "SaveOrganizationOnboardingSettings",
        ApiEndpointAccess.OrganizationAdministrator)]
    [InlineData(
        "GetMenuItemsByDonor",
        ApiEndpointAccess.Donor)]
    [InlineData(
        "GetDonorDashboardSettings",
        ApiEndpointAccess.Donor)]
    [InlineData(
        "GetDonationsByFund",
        ApiEndpointAccess.Authenticated)]
    [InlineData(
        "GetDonationsByMethod",
        ApiEndpointAccess.Authenticated)]
    [InlineData(
        "GetDonationsByMonth",
        ApiEndpointAccess.Authenticated)]
    [InlineData(
        "GetOrganization",
        ApiEndpointAccess.OrganizationUser)]
    [InlineData(
        "GetDashboardKpis",
        ApiEndpointAccess.OrganizationUser)]
    [InlineData(
        "BlobGet",
        ApiEndpointAccess.Authenticated)]
    [InlineData(
        "Logout",
        ApiEndpointAccess.Authenticated)]
    [InlineData(
        "LogMenuItemUsage",
        ApiEndpointAccess.Authenticated)]
    public void PrivilegedEndpointsHaveExpectedPolicies(
        string functionName,
        ApiEndpointAccess expectedAccess)
    {
        var policy =
            new ApiEndpointPolicyRegistry()
                .GetPolicy(
                    functionName);

        Assert.Equal(
            expectedAccess,
            policy.Access);
    }

    [Fact]
    public void DevelopmentLoginFailsClosedWithoutExplicitDevelopmentEnvironment()
    {
        var previous =
            Environment.GetEnvironmentVariable(
                "AZURE_FUNCTIONS_ENVIRONMENT");

        try
        {
            Environment.SetEnvironmentVariable(
                "AZURE_FUNCTIONS_ENVIRONMENT",
                null);

            var policy =
                new ApiEndpointPolicyRegistry()
                    .GetPolicy(
                        "Login");

            Assert.Equal(
                ApiEndpointAccess.Disabled,
                policy.Access);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "AZURE_FUNCTIONS_ENVIRONMENT",
                previous);
        }
    }
}
