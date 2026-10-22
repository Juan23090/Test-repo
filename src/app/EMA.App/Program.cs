using EMA.App;
using EMA.App.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder =
    WebAssemblyHostBuilder.CreateDefault(
        args);

builder.RootComponents.Add<App>(
    "#app");

builder.RootComponents.Add<HeadOutlet>(
    "head::after");

var apiBaseUrl =
    builder.Configuration["ApiBaseUrl"]
    ?? builder.HostEnvironment.BaseAddress;

builder.Services.AddAuthorizationCore(
    options =>
    {
        options.FallbackPolicy =
            new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
    });

RegisterApiHttpClient(
    builder,
    apiBaseUrl);

RegisterDonorHistoryHttpClient(
    builder,
    apiBaseUrl);

builder.Services.AddScoped<
    ICustomizationService,
    CustomizationService>();

builder.Services.AddScoped<
    IPADProcessingService,
    PADProcessingService>();

builder.Services.AddScoped<
    IDonorService,
    DonorService>();

builder.Services.AddScoped<
    IMailingListService,
    MailingListService>();

builder.Services.AddScoped<
    IDonationService,
    DonationService>();

builder.Services.AddScoped<
    IOrganizationService,
    OrganizationService>();

builder.Services.AddScoped<
    IOrganizationJourneyService,
    OrganizationJourneyService>();

builder.Services.AddScoped<
    ISetupWizardService,
    SetupWizardService>();

builder.Services.AddScoped<
    IOrganizationOnboardingService,
    OrganizationOnboardingService>();

builder.Services.AddScoped<
    IPublicOnboardingService,
    PublicOnboardingService>();

builder.Services.AddScoped<
    IDepositReportService,
    DepositReportService>();

builder.Services.AddScoped<
    IJournalEntryReportService,
    JournalEntryReportService>();

builder.Services.AddScoped<
    ITributeReportService,
    TributeReportService>();

builder.Services.AddScoped<
    IGiftReportService,
    GiftReportService>();

builder.Services.AddScoped<
    IEmaStatisticsService,
    EmaStatisticsService>();

builder.Services.AddScoped<
    IOrganizationStateService,
    OrganizationStateService>();

builder.Services.AddScoped<
    ILoginService,
    LoginService>();

builder.Services.AddSingleton<
    IUserSessionStateService,
    UserSessionStateService>();

builder.Services.AddScoped<
    ApiAccessTokenHandler>();

builder.Services.AddScoped<
    IChMSService,
    ChMSService>();

builder.Services.AddScoped<
    IBanksService,
    BanksService>();

builder.Services.AddScoped<
    IMenuNavigationService,
    ApiMenuNavigationService>();

builder.Services.AddScoped<
    ICategoryLandingPageService,
    CategoryLandingPageService>();

builder.Services.AddScoped<
    IAccountingSoftwareService,
    AccountingSoftwareService>();

builder.Services.AddScoped<
    IDateFormatService,
    DateFormatService>();

builder.Services.AddScoped<
    IBlobService,
    BlobService>();

builder.Services.AddScoped<
    IEmailVerificationService,
    EmailVerificationService>();

builder.Services.AddScoped<
    ISavingsCalculatorService,
    SavingsCalculatorService>();

builder.Services.AddScoped<
    ISandboxService,
    SandboxService>();

builder.Services.AddScoped<
    IReferralClientService,
    ReferralClientService>();

builder.Services.AddScoped<
    IScreenFeedbackService,
    ScreenFeedbackService>();

builder.Services.AddSingleton<
    IScreenActionUiState,
    ScreenActionUiState>();

builder.Services.AddSingleton<
    IMobileMenuStateService,
    MobileMenuStateService>();

await builder.Build().RunAsync();

static void RegisterDonorHistoryHttpClient(
    WebAssemblyHostBuilder builder,
    string apiBaseUrl)
{
    var baseUri =
        new Uri(
            apiBaseUrl.TrimEnd('/') + "/");

    builder.Services.AddHttpClient(
        "DonorHistoryApi",
        client =>
        {
            client.BaseAddress =
                baseUri;
        });
}

static void RegisterApiHttpClient(
    WebAssemblyHostBuilder builder,
    string apiBaseUrl)
{
    var baseUri =
        new Uri(
            apiBaseUrl.TrimEnd('/') + "/");

    builder.Services
        .AddHttpClient(
            "Api",
            client =>
            {
                client.BaseAddress =
                    baseUri;
            })
        .AddHttpMessageHandler<
            ApiAccessTokenHandler>();

    builder.Services.AddScoped(
        provider =>
        {
            return provider
                .GetRequiredService<
                    IHttpClientFactory>()
                .CreateClient(
                    "Api");
        });
}

