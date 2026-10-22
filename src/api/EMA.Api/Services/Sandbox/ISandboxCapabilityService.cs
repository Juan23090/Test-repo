namespace EMA.Api.Services.Sandbox;

public interface ISandboxCapabilityService
{
    Task<string> CreateAsync(
        int sandboxOrganizationID,
        CancellationToken cancellationToken = default);

    Task<int?> ResolveOrganizationIDAsync(
        string? capability,
        CancellationToken cancellationToken = default);
}
