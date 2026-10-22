namespace EMA.Api.Services.Security;

public sealed class ApiEndpointPolicyRegistry
{
    private const long DefaultBodyLimit =
        1_048_576;

    private const long BlobBodyLimit =
        TenantBlobUploadValidator
            .MaximumUploadBytes;

    private const long PdfBodyLimit =
        2_097_152;

    private static readonly HashSet<string>
        PublicFunctions =
        new(StringComparer.Ordinal)
        {
            "CompleteUnifiedLogin",
            "SearchPublicOrganizations",
            "StartDonorSelfRegistrationVerification",
            "UnifiedLoginLookup",
            "VerifyDonorSelfRegistration",
            "VerifyUnifiedLoginCode",
            "GetPublicRouteAccess",
            "GetPublicBrochureMenuItems",
            "GetPublicFaqs",
            "GetPublishedPublicFaqPage",
            "GetCurrentSandbox",
            "CreateSandbox",
            "ResumeSandbox",
            "GetPublicOnboardingLookups",
            "StartPublicOrganizationEmailVerification",
            "VerifyPublicOrganizationEmail",
            "SubmitPublicOrganizationRequest"
        };

    private static readonly HashSet<string>
        DevelopmentOnlyFunctions =
        new(StringComparer.Ordinal)
        {
            "GetLoginOptions",
            "Login",
            "DonorLogin"
        };

    private static readonly HashSet<string>
        DisabledFunctions =
        new(StringComparer.Ordinal)
        {
            "DonorOrganizationLoginLookup",
            "LookupDonorLogin",
            "LookupLoginUser",
            "StartDonorLoginVerification",
            "StartOrganizationLoginVerification",
            "TestEmail",
            "ResumeSandboxByPhone",
            "VerifyDonorLoginCode",
            "VerifyOrganizationLoginCode"
        };

    private static readonly HashSet<string>
        SelfContainedDonorTokenFunctions =
        new(StringComparer.Ordinal)
        {
            "GetDonorHistory",
            "GetDonorReceipts",
            "GetDonorDirectedChanges",
            "SaveDonorDirectedChanges"
        };

    private static readonly HashSet<string>
        DonorFunctions =
        new(StringComparer.Ordinal)
        {
            "GetDonorCategoryLandingPage",
            "GetDonorRouteAccess",
            "GetDonorDashboardSettings",
            "GetMenuItemsByDonor"
        };

    private static readonly HashSet<string>
        AuthenticatedFunctions =
        new(StringComparer.Ordinal)
        {
            "BlobGet",
            "GetDonationsByFund",
            "GetDonationsByMethod",
            "GetDonationsByMonth",
            "Logout",
            "LogMenuItemUsage"
        };

    private static readonly HashSet<string>
        PlatformAdministratorFunctions =
        new(StringComparer.Ordinal)
        {
            "CreateOrganization",
            "CreateOrganizationJourneyEntry",
            "GetAllOrganizations",
            "GetOrganizationFollowUpQueue",
            "GetOrganizationJourneyActivityTypes",
            "GetOrganizationJourneyHistory",
            "GetOrganizationStatuses",
            "UpdateOrganizationJourneyDetails",
            "GetEmaStatistics",
            "GetMenuItemCategories",
            "GetMenuItemRows",
            "GetMenuItemTypes",
            "GetMenuItemResourceTypes",
            "GenerateMenuItemAudio",
            "GetMenuItems",
            "GetSetupWizardForInsider",
            "GetBankLookups",
            "PostBank",
            "PutBank",
            "PostBankTransactionType",
            "PutBankTransactionType",
            "PostBankKeyPhrase",
            "PutBankKeyPhrase",
            "PostChMS",
            "PutChMS",
            "SaveMenuItem",
            "SaveMenuItemResources"
        };

    private static readonly HashSet<string>
        PlatformCrossTenantOrganizationUserFunctions =
        new(StringComparer.Ordinal)
        {
            "GetOrganizationJourneyFeedback"
        };

    private static readonly HashSet<string>
        OrganizationAdministratorFunctions =
        new(StringComparer.Ordinal)
        {
            "BlobDelete",
            "BlobPut",
            "CopyDefaultEntities",
            "DeleteCampus",
            "DeleteFund",
            "DeleteMethodOfGiving",
            "GetOrganizationAudits",
            "GetOrganizationOnboardingSettings",
            "GetUsers",
            "GetRoles",
            "MergeData",
            "PostCampus",
            "PostDonationSource",
            "PostDonorStatus",
            "PostEvent",
            "PostFund",
            "PostInteractionType",
            "PostMethodOfGiving",
            "PostOrganizationAudit",
            "PostOrgSettings",
            "PostAssignYearEndReceipts",
            "PostTributeType",
            "PutCampus",
            "PutDonationSource",
            "PutDonorStatus",
            "PutEntityAlias",
            "PutEvent",
            "PutFund",
            "PutInteractionType",
            "PutMethodOfGiving",
            "PutOrgAnonymousDonor",
            "PutTributeType",
            "SaveKpiHistoryDays",
            "SaveSavingsCalculatorSettings",
            "SaveUser",
            "SaveOrganizationOnboardingSettings",
            "UpdateOrganization",
            "UpdateOrgJESettings"
        };

    private static readonly IReadOnlyDictionary<
        string,
        long> FunctionBodyLimits =
        new Dictionary<string, long>(
            StringComparer.Ordinal)
        {
            ["BlobPut"] =
                BlobBodyLimit,

            ["PostSavingsCalculatorPdf"] =
                PdfBodyLimit,

            ["PostYearEndReceiptPdf"] =
                PdfBodyLimit
        };

    public ApiEndpointPolicy GetPolicy(
        string functionName)
    {
        if (string.IsNullOrWhiteSpace(
                functionName))
        {
            return new ApiEndpointPolicy(
                ApiEndpointAccess.Disabled);
        }

        if (DisabledFunctions.Contains(
                functionName))
        {
            return new ApiEndpointPolicy(
                ApiEndpointAccess.Disabled);
        }

        if (DevelopmentOnlyFunctions.Contains(
                functionName))
        {
            return IsDevelopment()
                ? new ApiEndpointPolicy(
                    ApiEndpointAccess.Public)
                : new ApiEndpointPolicy(
                    ApiEndpointAccess.Disabled);
        }

        if (PublicFunctions.Contains(
                functionName))
        {
            return new ApiEndpointPolicy(
                ApiEndpointAccess.Public,
                GetBodyLimit(
                    functionName));
        }

        if (SelfContainedDonorTokenFunctions
            .Contains(
                functionName))
        {
            return new ApiEndpointPolicy(
                ApiEndpointAccess
                    .SelfContainedDonorToken,
                GetBodyLimit(
                    functionName));
        }

        if (DonorFunctions.Contains(
                functionName))
        {
            return new ApiEndpointPolicy(
                ApiEndpointAccess.Donor,
                GetBodyLimit(
                    functionName));
        }

        if (AuthenticatedFunctions.Contains(
                functionName))
        {
            return new ApiEndpointPolicy(
                ApiEndpointAccess.Authenticated,
                GetBodyLimit(
                    functionName));
        }

        if (PlatformAdministratorFunctions
            .Contains(
                functionName))
        {
            return new ApiEndpointPolicy(
                ApiEndpointAccess
                    .PlatformAdministrator,
                GetBodyLimit(
                    functionName));
        }

        if (PlatformCrossTenantOrganizationUserFunctions
            .Contains(
                functionName))
        {
            return new ApiEndpointPolicy(
                ApiEndpointAccess.OrganizationUser,
                GetBodyLimit(
                    functionName),
                AllowPlatformCrossTenant: true);
        }

        if (OrganizationAdministratorFunctions
            .Contains(
                functionName))
        {
            return new ApiEndpointPolicy(
                ApiEndpointAccess
                    .OrganizationAdministrator,
                GetBodyLimit(
                    functionName));
        }

        // New HTTP functions are protected by default.
        return new ApiEndpointPolicy(
            ApiEndpointAccess.OrganizationUser,
            GetBodyLimit(
                functionName));
    }

    private static long GetBodyLimit(
        string functionName)
    {
        return FunctionBodyLimits.TryGetValue(
            functionName,
            out var configuredLimit)
            ? configuredLimit
            : DefaultBodyLimit;
    }

    private static bool IsDevelopment()
    {
        return ApiRuntimeEnvironment
            .IsDevelopment;
    }
}
