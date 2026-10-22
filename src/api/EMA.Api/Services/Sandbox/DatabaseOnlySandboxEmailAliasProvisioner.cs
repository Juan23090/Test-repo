using Microsoft.Extensions.Logging;

namespace EMA.Api.Services.Sandbox;

/// <summary>
/// The permanent provisioner for the built-in sandbox experience. The logical
/// sandbox address is stored by the sandbox creation procedure and is used by
/// direct MIME ingestion to resolve the correct sandbox organization. No real
/// mailbox or Microsoft 365 alias is required.
/// </summary>
public sealed class DatabaseOnlySandboxEmailAliasProvisioner :
    ISandboxEmailAliasProvisioner
{
    private readonly ILogger<DatabaseOnlySandboxEmailAliasProvisioner> _logger;

    public DatabaseOnlySandboxEmailAliasProvisioner(
        ILogger<DatabaseOnlySandboxEmailAliasProvisioner> logger)
    {
        _logger = logger;
    }

    public Task EnsureAliasAsync(
        int sandboxOrganizationID,
        string sandboxEmailAddress,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Logical sandbox eTransfer address {SandboxEmailAddress} is active for OrganizationID {OrganizationID}. " +
            "Built-in sandbox test emails are processed through direct EMA ingestion.",
            sandboxEmailAddress,
            sandboxOrganizationID);

        return Task.CompletedTask;
    }

    public Task RemoveAliasAsync(
        int sandboxOrganizationID,
        string sandboxEmailAddress,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Logical sandbox eTransfer address {SandboxEmailAddress} was removed with OrganizationID {OrganizationID}.",
            sandboxEmailAddress,
            sandboxOrganizationID);

        return Task.CompletedTask;
    }
}