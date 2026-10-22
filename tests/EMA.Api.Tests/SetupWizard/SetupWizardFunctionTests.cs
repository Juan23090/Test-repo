using EMA.Api.DTOs;
using EMA.Api.Functions.SetupWizard;
using EMA.Api.Services.Navigation;
using EMA.Api.Services.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Reflection;

namespace EMA.Api.Tests.SetupWizard;

public sealed class SetupWizardFunctionTests
{
    [Fact]
    public void OrganizationWizardHasExpectedRoute()
    {
        var method =
            typeof(GetSetupWizard).GetMethod(
                nameof(GetSetupWizard.Run));

        Assert.NotNull(method);

        var function =
            method!.GetCustomAttribute<FunctionAttribute>();

        Assert.NotNull(function);
        Assert.Equal(
            "GetSetupWizard",
            function!.Name);

        var trigger =
            method.GetParameters()
                .Select(parameter =>
                    parameter.GetCustomAttribute<HttpTriggerAttribute>())
                .FirstOrDefault(attribute =>
                    attribute is not null);

        Assert.NotNull(trigger);
        Assert.Equal(
            "organization/{orgID:int}/setup-wizard",
            trigger!.Route);
    }

    [Fact]
    public void InsiderWizardHasExpectedRoute()
    {
        var method =
            typeof(GetSetupWizardForInsider).GetMethod(
                nameof(GetSetupWizardForInsider.Run));

        Assert.NotNull(method);

        var function =
            method!.GetCustomAttribute<FunctionAttribute>();

        Assert.NotNull(function);
        Assert.Equal(
            "GetSetupWizardForInsider",
            function!.Name);

        var trigger =
            method.GetParameters()
                .Select(parameter =>
                    parameter.GetCustomAttribute<HttpTriggerAttribute>())
                .FirstOrDefault(attribute =>
                    attribute is not null);

        Assert.NotNull(trigger);
        Assert.Equal(
            "platform/setup-wizard/organizations/{orgID:int}",
            trigger!.Route);
    }

    [Fact]
    public async Task OrganizationAdministratorReceivesAllWizardStepsMode()
    {
        var query =
            new FakeSetupWizardQuery
            {
                Result = WizardResult(18)
            };

        var function =
            new GetSetupWizard(
                NullLogger<GetSetupWizard>.Instance,
                query,
                CreateAuthorization());

        var request =
            Request(
                Identity(
                    organizationID: 18,
                    userID: 7,
                    roleIDs: [10],
                    categoryIDs: [20]));

        var result =
            await function.Run(
                request,
                orgID: 18);

        var ok =
            Assert.IsType<OkObjectResult>(
                result);

        Assert.Same(
            query.Result,
            ok.Value);
        Assert.True(
            query.IncludeAllWizardSteps);
        Assert.Equal(
            18,
            query.OrganizationID);
        Assert.Equal(
            7,
            query.UserID);
    }

    [Fact]
    public async Task RegularOrganizationUserUsesRoleFilteredWizardMode()
    {
        var query =
            new FakeSetupWizardQuery
            {
                Result = WizardResult(18)
            };

        var function =
            new GetSetupWizard(
                NullLogger<GetSetupWizard>.Instance,
                query,
                CreateAuthorization());

        var request =
            Request(
                Identity(
                    organizationID: 18,
                    userID: 8,
                    roleIDs: [20],
                    categoryIDs: [20]));

        var result =
            await function.Run(
                request,
                orgID: 18);

        Assert.IsType<OkObjectResult>(
            result);
        Assert.False(
            query.IncludeAllWizardSteps);
    }

    [Fact]
    public async Task MissingOrganizationReturnsNotFound()
    {
        var query =
            new FakeSetupWizardQuery();

        var function =
            new GetSetupWizardForInsider(
                NullLogger<GetSetupWizardForInsider>.Instance,
                query);

        var result =
            await function.Run(
                new DefaultHttpContext().Request,
                orgID: 999);

        Assert.IsType<NotFoundObjectResult>(
            result);
    }

    [Fact]
    public async Task InsiderQueryUsesSelectedOrganization()
    {
        var query =
            new FakeSetupWizardQuery
            {
                Result = WizardResult(44)
            };

        var function =
            new GetSetupWizardForInsider(
                NullLogger<GetSetupWizardForInsider>.Instance,
                query);

        var result =
            await function.Run(
                new DefaultHttpContext().Request,
                orgID: 44);

        Assert.IsType<OkObjectResult>(
            result);
        Assert.True(
            query.InsiderCalled);
        Assert.Equal(
            44,
            query.OrganizationID);
    }

    private static ApiRequestBoundaryValidator CreateAuthorization()
    {
        var configuration =
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["PLATFORM_ORGANIZATION_ID"] = "1",
                        ["PLATFORM_ADMIN_CATEGORY_ID"] = "90",
                        ["ORGANIZATION_ADMIN_ROLE_IDS"] = "10"
                    })
                .Build();

        var tenantAuthorization =
            new Mock<ITenantResourceAuthorizationService>();

        return new ApiRequestBoundaryValidator(
            configuration,
            tenantAuthorization.Object);
    }

    private static HttpRequest Request(
        ApiRequestIdentity identity)
    {
        var context =
            new DefaultHttpContext();

        context.User =
            identity.ToClaimsPrincipal();

        return context.Request;
    }

    private static ApiRequestIdentity Identity(
        int organizationID,
        int userID,
        IReadOnlyList<int> roleIDs,
        IReadOnlyList<int> categoryIDs)
    {
        return new ApiRequestIdentity(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ApiActorType.OrganizationUser,
            organizationID,
            userID,
            DonorID: null,
            roleIDs,
            categoryIDs,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddMinutes(15));
    }

    private static SetupWizardResponseDto WizardResult(
        int organizationID)
    {
        return new SetupWizardResponseDto
        {
            EMAOrganizationID = organizationID,
            OrganizationName = "Test Organization",
            Steps =
            [
                new SetupWizardStepDto
                {
                    MenuItemID = 19100,
                    ScreenTitle = "Set up Organization",
                    Url = "/basic-information"
                }
            ]
        };
    }

    private sealed class FakeSetupWizardQuery : ISetupWizardQuery
    {
        public SetupWizardResponseDto? Result { get; init; }

        public int OrganizationID { get; private set; }

        public int UserID { get; private set; }

        public bool IncludeAllWizardSteps { get; private set; }

        public bool InsiderCalled { get; private set; }

        public Task<SetupWizardResponseDto?> GetForOrganizationUserAsync(
            int organizationID,
            int userID,
            bool includeAllWizardSteps,
            CancellationToken cancellationToken = default)
        {
            OrganizationID = organizationID;
            UserID = userID;
            IncludeAllWizardSteps = includeAllWizardSteps;

            return Task.FromResult(Result);
        }

        public Task<SetupWizardResponseDto?> GetForPlatformAdministratorAsync(
            int organizationID,
            CancellationToken cancellationToken = default)
        {
            InsiderCalled = true;
            OrganizationID = organizationID;

            return Task.FromResult(Result);
        }
    }
}
