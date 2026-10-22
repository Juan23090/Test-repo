using EMA.App.Models;

namespace EMA.App.Services;

public interface ISandboxService
{
    Task<SandboxServiceResult<CurrentSandboxResponse>> GetCurrentSandboxAsync(
        CancellationToken cancellationToken = default);

    Task<SandboxServiceResult<CreateSandboxResponse>> CreateSandboxAsync(
        CancellationToken cancellationToken = default);

    Task<SandboxServiceResult<LoginResponse>> ResumeSandboxAsync(
        int sandboxOrganizationID,
        CancellationToken cancellationToken = default);

    Task<SandboxServiceResult<SandboxTestEmailDraft>> GetTestEmailDraftAsync(
        int sandboxOrganizationID,
        CancellationToken cancellationToken = default);

    Task<SandboxServiceResult<SendSandboxTestEmailResponse>> SendTestEmailAsync(
        SendSandboxTestEmailRequest request,
        CancellationToken cancellationToken = default);
}