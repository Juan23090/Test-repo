using EMA.Api.Services;
using EMA.Api.Services.Authentication;
using EMA.Api.Services.Audio;
using EMA.Api.Services.Email;
using EMA.Api.Services.EmailAliases;
using EMA.Api.Services.Navigation;
using EMA.Api.Services.Onboarding;
using EMA.Api.Services.Referrals;
using EMA.Api.Services.Sandbox;
using EMA.Api.Services.ScreenFeedback;
using EMA.Api.Services.Security;
using EMA_Api.Services.EmailAliases;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host =
    new HostBuilder()
        .ConfigureFunctionsWebApplication(
            worker =>
            {
                worker.UseMiddleware<
                    ApiSecurityMiddleware>();
            })
        .ConfigureServices(services =>
        {
            services
                .AddApplicationInsightsTelemetryWorkerService();

            services
                .ConfigureFunctionsApplicationInsights();

            services.AddSingleton<
                ApiEndpointPolicyRegistry>();

            services.AddHostedService<
                ProductionSecurityConfigurationValidator>();

            services.AddSingleton<
                ITenantResourceAuthorizationService,
                SqlTenantResourceAuthorizationService>();

            services.AddSingleton<
                ITenantBlobLegacyAuthorizationService,
                SqlTenantBlobLegacyAuthorizationService>();

            services.AddSingleton<
                ApiRequestBoundaryValidator>();

            services.AddSingleton<
                IMenuItemAccessService,
                SqlMenuItemAccessService>();

            services.AddSingleton<
                ISetupWizardQuery,
                SqlSetupWizardQuery>();

            services.AddSingleton<
                IOrganizationOnboardingRepository,
                SqlOrganizationOnboardingRepository>();

            services.AddSingleton<
                IPublicOrganizationRequestRepository,
                SqlPublicOrganizationRequestRepository>();

            services.AddSingleton<
                IScreenFeedbackRepository,
                SqlScreenFeedbackRepository>();

            services.AddSingleton<
                IScreenFeedbackAuthorizationService,
                SqlScreenFeedbackAuthorizationService>();

            services.AddSingleton<
                ApiCorsPolicy>();

            services.AddSingleton<
                IApiRequestRateLimiter,
                SqlApiRequestRateLimiter>();

            services.AddSingleton<
                IApiAccessTokenService,
                ApiAccessTokenService>();

            services.AddSingleton<
                IApiSessionService,
                SqlApiSessionService>();

            services.AddSingleton<
                ITrustedDeviceService,
                SqlTrustedDeviceService>();

            services.AddSingleton<
                EmailParserService>();

            services.AddSingleton<
                IEmailParserService,
                EmailParserService>();

            services.AddSingleton<
                IBlobStorageService,
                BlobStorageService>();

            services.AddSingleton<
                ITextToSpeechService,
                GoogleCloudTextToSpeechService>();

            services.AddSingleton<
                IMenuItemAudioGenerationPersistence,
                SqlMenuItemAudioGenerationPersistence>();

            services.AddScoped<
                IDonationEmailIngestionService,
                DonationEmailIngestionService>();

            services.AddHttpClient<
                IGraphEmailService,
                GraphEmailService>();

            services.AddScoped<
                IEmailService,
                ACSEmailService>();

            services.AddSingleton<
                IDonorAccessTokenService,
                DonorAccessTokenService>();

            services.AddSingleton<
                IEmailTemplateService,
                EmailTemplateService>();

            services.AddScoped<
                IEmailNotificationService,
                EmailNotificationService>();

            services.AddSingleton<
                IEtransferEmailAliasService,
                ExchangeOnlineEtransferEmailAliasService>();

            services.AddSingleton<
                ITwoFactorCodeStore,
                SqlTwoFactorCodeStore>();

            services.AddSingleton<
                IPendingDonorRegistrationStore,
                SqlPendingDonorRegistrationStore>();

            services.AddScoped<
                ITwoFactorEmailSender,
                AcsTwoFactorEmailSender>();

            services.AddSingleton<
                ISandboxEmailAliasProvisioner,
                DatabaseOnlySandboxEmailAliasProvisioner>();

            services.AddSingleton<
                ISandboxCapabilityService,
                SqlSandboxCapabilityService>();

            services.AddScoped<
                ISandboxService,
                SandboxService>();

            services.AddScoped<
                IReferralService,
                ReferralService>();
        })
        .Build();

host.Run();
