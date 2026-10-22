using EMA.Api.DTOs;

namespace EMA.Api.Services.Sandbox;

public interface ISandboxService
{
	Task<SandboxWorkspaceSummaryDto?> GetCurrentSandboxAsync(
		string clientIPAddress,
		CancellationToken cancellationToken = default);

	Task<SandboxWorkspaceSummaryDto?> GetSandboxAsync(
		int sandboxOrganizationID,
		CancellationToken cancellationToken = default);

	Task<SandboxWorkspaceSummaryDto> CreateSandboxAsync(
		string clientIPAddress,
		CancellationToken cancellationToken = default);

	Task<LoginResponseDto?> ResumeSandboxAsync(
		int sandboxOrganizationID,
		string clientIPAddress,
		CancellationToken cancellationToken = default);

	Task<LoginResponseDto?> ResumeSandboxByPhoneAsync(
		string phoneNumber,
		string clientIPAddress,
		CancellationToken cancellationToken = default);

	Task<SandboxTestEmailDraftDto?> GetTestEmailDraftAsync(
		int sandboxOrganizationID,
		string clientIPAddress,
		CancellationToken cancellationToken = default);

	Task<SendSandboxTestEmailResponseDto> SendTestEmailAsync(
		SendSandboxTestEmailRequestDto request,
		string clientIPAddress,
		CancellationToken cancellationToken = default);

	Task<SandboxCleanupResultDto> DeleteExpiredSandboxesAsync(
        int? inactiveDays = null,
        CancellationToken cancellationToken = default);
}
