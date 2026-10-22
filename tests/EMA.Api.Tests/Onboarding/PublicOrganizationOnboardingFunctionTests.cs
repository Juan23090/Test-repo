using System.Text;
using EMA.Api.DTOs;
using EMA.Api.Functions.Onboarding;
using EMA.Api.Services.Authentication;
using EMA.Api.Services.Onboarding;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace EMA.Api.Tests.Onboarding;

public sealed class PublicOrganizationOnboardingFunctionTests
{
    [Fact]
    public async Task InvalidStatusIsRejectedBeforePersistence()
    {
        var repository = new FakeRepository();
        var codeStore = new FakeCodeStore { ConsumeResult = true };
        var function = CreateFunction(repository, codeStore);
        var request = ValidRequest();
        request.OrganizationStatusID = 20;

        var result = await function.SubmitOrganizationAsync(HttpRequest(Json(request)));

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.False(repository.CreateCalled);
        Assert.False(codeStore.ConsumeCalled);
    }

    [Fact]
    public async Task UnverifiedEmailIsRejectedBeforePersistence()
    {
        var repository = new FakeRepository();
        var codeStore = new FakeCodeStore { ConsumeResult = false };
        var function = CreateFunction(repository, codeStore);

        var result = await function.SubmitOrganizationAsync(HttpRequest(Json(ValidRequest())));

        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.False(repository.CreateCalled);
        Assert.True(codeStore.ConsumeCalled);
    }

    [Fact]
    public async Task ValidVerifiedRequestReachesPersistenceAndReturnsCreated()
    {
        var repository = new FakeRepository { CreatedOrganizationID = 321 };
        var codeStore = new FakeCodeStore { ConsumeResult = true };
        var function = CreateFunction(repository, codeStore);

        var result = await function.SubmitOrganizationAsync(HttpRequest(Json(ValidRequest())));

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.True(repository.CreateCalled);
        Assert.True(codeStore.ConsumeCalled);
        Assert.Equal(40, repository.LastRequest!.OrganizationStatusID);
        Assert.Equal("SK", repository.LastRequest.ProvinceStateCode);
    }

    [Fact]
    public async Task PersistenceFailureDoesNotReportSuccess()
    {
        var repository = new FakeRepository
        {
            CreateException = new InvalidOperationException("simulated database failure")
        };
        var codeStore = new FakeCodeStore { ConsumeResult = true };
        var function = CreateFunction(repository, codeStore);

        var result = await function.SubmitOrganizationAsync(HttpRequest(Json(ValidRequest())));

        var error = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, error.StatusCode);
        Assert.True(repository.CreateCalled);
    }

    [Fact]
    public async Task PublicVerificationEndpointRejectsOtherPurposes()
    {
        var repository = new FakeRepository();
        var codeStore = new FakeCodeStore();
        var function = CreateFunction(repository, codeStore);

        var result = await function.StartEmailVerificationAsync(
            HttpRequest("""
                {"purpose":11,"actorID":123,"emailAddress":"admin@example.org"}
                """));

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.False(codeStore.CreateCalled);
    }

    [Fact]
    public async Task PublicLookupsComeFromRepository()
    {
        var repository = new FakeRepository
        {
            Lookups = new PublicOnboardingLookupsDto
            {
                ChMSOptions =
                [
                    new PublicChMSOptionDto { ChMSID = 7, ChMSName = "Example" }
                ]
            }
        };
        var function = CreateFunction(repository, new FakeCodeStore());

        var result = await function.GetLookupsAsync(HttpRequest(string.Empty));

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(repository.Lookups, ok.Value);
    }

    private static PublicOrganizationOnboardingFunctions CreateFunction(
        FakeRepository repository,
        FakeCodeStore codeStore) =>
        new(
            NullLogger<PublicOrganizationOnboardingFunctions>.Instance,
            repository,
            codeStore,
            new FakeEmailSender());

    private static PublicOrganizationRequestDto ValidRequest() =>
        new()
        {
            OrganizationName = "Example Organization",
            AdminName = "Example Admin",
            AdminEmail = "Admin@Example.org",
            AdminEmailVerificationToken = "verified-token",
            AdminPhone = "3065550100",
            Address = "1 Main Street",
            City = "Saskatoon",
            ProvinceStateCode = "Saskatchewan",
            PostalCode = "S7K 0J5",
            AnnualBudget = "50k-100k",
            ChMSID = 1,
            AccountingSoftwareID = 1,
            OrganizationStatusID = 40
        };

    private static HttpRequest HttpRequest(string json)
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        context.Request.ContentType = "application/json";
        return context.Request;
    }

    private static string Json(PublicOrganizationRequestDto request) =>
        System.Text.Json.JsonSerializer.Serialize(request);

    private sealed class FakeRepository : IPublicOrganizationRequestRepository
    {
        public PublicOnboardingLookupsDto Lookups { get; init; } = new();
        public int CreatedOrganizationID { get; init; } = 123;
        public Exception? CreateException { get; init; }
        public bool CreateCalled { get; private set; }
        public PublicOrganizationRequestDto? LastRequest { get; private set; }

        public Task<PublicOnboardingLookupsDto> GetLookupsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Lookups);

        public Task<int> CreateAsync(
            PublicOrganizationRequestDto request,
            CancellationToken cancellationToken = default)
        {
            CreateCalled = true;
            LastRequest = request;

            if (CreateException is not null)
            {
                throw CreateException;
            }

            return Task.FromResult(CreatedOrganizationID);
        }
    }

    private sealed class FakeCodeStore : ITwoFactorCodeStore
    {
        public bool ConsumeResult { get; init; }
        public bool ConsumeCalled { get; private set; }
        public bool CreateCalled { get; private set; }

        public Task<TwoFactorCodeRecord> CreateCodeAsync(
            TwoFactorPurpose purpose,
            int actorID,
            string emailAddress,
            CancellationToken cancellationToken = default)
        {
            CreateCalled = true;
            return Task.FromResult(new TwoFactorCodeRecord
            {
                VerificationID = "verification-id",
                Code = "123456",
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10)
            });
        }

        public Task<bool> VerifyCodeAsync(
            string verificationID,
            TwoFactorPurpose purpose,
            int actorID,
            string? emailAddress,
            string code,
            bool consumeOnSuccess = true,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> ConsumeVerifiedCodeAsync(
            string verificationID,
            TwoFactorPurpose purpose,
            int actorID,
            string emailAddress,
            CancellationToken cancellationToken = default)
        {
            ConsumeCalled = true;
            return Task.FromResult(ConsumeResult);
        }

        public Task RevokeCodeAsync(
            string verificationID,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeEmailSender : ITwoFactorEmailSender
    {
        public Task SendCodeAsync(
            string emailAddress,
            string code,
            VerificationEmailKind emailKind,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SendDonorRegistrationCodeAsync(
            string emailAddress,
            string code,
            string phoneNumber,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
