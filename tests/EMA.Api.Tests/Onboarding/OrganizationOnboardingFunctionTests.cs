using System.Reflection;
using System.Text;
using EMA.Api.DTOs;
using EMA.Api.Functions.Onboarding;
using EMA.Api.Services.Onboarding;
using EMA.Api.Services.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging.Abstractions;

namespace EMA.Api.Tests.Onboarding;

public sealed class OrganizationOnboardingFunctionTests
{
    [Theory]
    [InlineData(nameof(OrganizationOnboardingFunctions.GetAsync), "GetOrganizationOnboardingSettings", "organization/{orgID:int}/onboarding/donation-attributes")]
    [InlineData(nameof(OrganizationOnboardingFunctions.SaveAsync), "SaveOrganizationOnboardingSettings", "organization/{orgID:int}/onboarding/donation-attributes/{section}")]
    public void FunctionsHaveExpectedRoutes(
        string methodName,
        string functionName,
        string route)
    {
        var method =
            typeof(OrganizationOnboardingFunctions)
                .GetMethod(methodName);

        Assert.NotNull(method);

        var function =
            method!.GetCustomAttribute<FunctionAttribute>();

        Assert.NotNull(function);
        Assert.Equal(functionName, function!.Name);

        var trigger =
            method.GetParameters()
                .Select(parameter =>
                    parameter.GetCustomAttribute<HttpTriggerAttribute>())
                .FirstOrDefault(attribute =>
                    attribute is not null);

        Assert.NotNull(trigger);
        Assert.Equal(route, trigger!.Route);
    }

    [Fact]
    public async Task SaveUsesAuthenticatedActorAndRequestedOrganization()
    {
        var repository =
            new FakeRepository
            {
                SaveResult =
                    OrganizationOnboardingSaveResult.Success()
            };

        var function =
            new OrganizationOnboardingFunctions(
                NullLogger<OrganizationOnboardingFunctions>.Instance,
                repository);

        var request =
            Request(
                "{\"EMAEventIDForPADs\":123}",
                Identity(
                    organizationID: 18,
                    userID: 7));

        var result =
            await function.SaveAsync(
                request,
                orgID: 18,
                section: "pads");

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(18, repository.OrganizationID);
        Assert.Equal(7, repository.ActorUserID);
        Assert.Equal("pads", repository.Section);
        Assert.Equal(123, repository.Request?.EMAEventIDForPADs);
    }

    [Fact]
    public async Task InvalidJsonIsRejectedBeforePersistence()
    {
        var repository =
            new FakeRepository();

        var function =
            new OrganizationOnboardingFunctions(
                NullLogger<OrganizationOnboardingFunctions>.Instance,
                repository);

        var result =
            await function.SaveAsync(
                Request("{not-json", Identity(18, 7)),
                orgID: 18,
                section: "pads");

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.False(repository.SaveCalled);
    }

    [Fact]
    public async Task RepositoryValidationFailureReturnsBadRequest()
    {
        var repository =
            new FakeRepository
            {
                SaveResult =
                    OrganizationOnboardingSaveResult.Invalid(
                        "The selected fund does not belong to this organization.")
            };

        var function =
            new OrganizationOnboardingFunctions(
                NullLogger<OrganizationOnboardingFunctions>.Instance,
                repository);

        var result =
            await function.SaveAsync(
                Request("{}", Identity(18, 7)),
                orgID: 18,
                section: "etransfers");

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task MissingCreditDebitSchemaReturnsConflict()
    {
        var repository =
            new FakeRepository
            {
                SaveResult =
                    OrganizationOnboardingSaveResult.SchemaConflict(
                        "Credit and Debit Card defaults are unavailable.")
            };

        var function =
            new OrganizationOnboardingFunctions(
                NullLogger<OrganizationOnboardingFunctions>.Instance,
                repository);

        var result =
            await function.SaveAsync(
                Request("{}", Identity(18, 7)),
                orgID: 18,
                section: "crdb");

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public void EtransferEmailPrerequisitesRequireBothMethodAndFund()
    {
        Assert.False(
            SqlOrganizationOnboardingRepository.HasEtransferPrerequisites(
                new SaveOrganizationOnboardingSettingsRequestDto()));

        Assert.False(
            SqlOrganizationOnboardingRepository.HasEtransferPrerequisites(
                new SaveOrganizationOnboardingSettingsRequestDto
                {
                    EMAMethodOfGivingIDForETransfers = 10
                }));

        Assert.False(
            SqlOrganizationOnboardingRepository.HasEtransferPrerequisites(
                new SaveOrganizationOnboardingSettingsRequestDto
                {
                    EMAFundIDForEtransfer = 20
                }));

        Assert.True(
            SqlOrganizationOnboardingRepository.HasEtransferPrerequisites(
                new SaveOrganizationOnboardingSettingsRequestDto
                {
                    EMAMethodOfGivingIDForETransfers = 10,
                    EMAFundIDForEtransfer = 20
                }));
    }

    [Fact]
    public void DepositValuesOverDatabaseLengthAreRejected()
    {
        var result =
            SqlOrganizationOnboardingRepository.ValidateRequestValues(
                "deposit",
                new SaveOrganizationOnboardingSettingsRequestDto
                {
                    BankAccountNumber = new string('1', 256)
                });

        Assert.False(result.Succeeded);
        Assert.Contains("255", result.Message);
    }

    [Theory]
    [InlineData("anonymous", "anonymous")]
    [InlineData("deposit", "deposit")]
    [InlineData("deposit-slip", "deposit")]
    [InlineData("etransfers", "etransfers")]
    [InlineData("pads", "pads")]
    [InlineData("crdb", "crdb")]
    public void KnownSectionsNormalize(
        string input,
        string expected)
    {
        Assert.Equal(
            expected,
            SqlOrganizationOnboardingRepository.NormalizeSection(input));
    }

    private static HttpRequest Request(
        string body,
        ApiRequestIdentity identity)
    {
        var context =
            new DefaultHttpContext();

        context.User =
            identity.ToClaimsPrincipal();

        context.Request.Body =
            new MemoryStream(
                Encoding.UTF8.GetBytes(body));

        return context.Request;
    }

    private static ApiRequestIdentity Identity(
        int organizationID,
        int userID) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ApiActorType.OrganizationUser,
            organizationID,
            userID,
            DonorID: null,
            RoleIDs: [10],
            AllowedCategoryIDs: [20],
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddMinutes(15));

    private sealed class FakeRepository : IOrganizationOnboardingRepository
    {
        public OrganizationOnboardingSaveResult SaveResult { get; init; } =
            OrganizationOnboardingSaveResult.Success();

        public bool SaveCalled { get; private set; }
        public int OrganizationID { get; private set; }
        public int ActorUserID { get; private set; }
        public string? Section { get; private set; }
        public SaveOrganizationOnboardingSettingsRequestDto? Request { get; private set; }

        public Task<OrganizationOnboardingSettingsDto?> GetAsync(
            int organizationID,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<OrganizationOnboardingSettingsDto?>(
                new OrganizationOnboardingSettingsDto
                {
                    EMAOrganizationID = organizationID
                });

        public Task<OrganizationOnboardingSaveResult> SaveAsync(
            int organizationID,
            int actorUserID,
            string section,
            SaveOrganizationOnboardingSettingsRequestDto request,
            CancellationToken cancellationToken = default)
        {
            SaveCalled = true;
            OrganizationID = organizationID;
            ActorUserID = actorUserID;
            Section = section;
            Request = request;

            return Task.FromResult(SaveResult);
        }
    }
}
