namespace EMA.Api.Services.Sandbox;

/// <summary>
/// Maintains the logical sandbox eTransfer address associated with a sandbox
/// organization. The built-in sandbox email editor uses direct ingestion, so
/// the address does not need to be provisioned as a real external mailbox.
/// This boundary remains available if external alias routing is added later.
/// </summary>
public interface ISandboxEmailAliasProvisioner
{
    Task EnsureAliasAsync(
        int sandboxOrganizationID,
        string sandboxEmailAddress,
        CancellationToken cancellationToken = default);

    Task RemoveAliasAsync(
        int sandboxOrganizationID,
        string sandboxEmailAddress,
        CancellationToken cancellationToken = default);
}