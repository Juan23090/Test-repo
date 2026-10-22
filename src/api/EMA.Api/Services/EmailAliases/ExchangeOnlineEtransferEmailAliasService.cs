using EMA_Api.Services.EmailAliases;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Security.Cryptography.X509Certificates;

namespace EMA.Api.Services.EmailAliases;

public sealed class ExchangeOnlineEtransferEmailAliasService :
    IEtransferEmailAliasService
{
    private static readonly SemaphoreSlim OperationLock =
        new(1, 1);

    private readonly ILogger<ExchangeOnlineEtransferEmailAliasService> _logger;
    private readonly string _provisioningMode;
    private readonly string? _organization;
    private readonly string? _mailboxIdentity;
    private readonly string? _modulePath;
    private readonly string? _appID;
    private readonly string? _certificateBase64;
    private readonly string? _certificatePath;
    private readonly string? _certificatePassword;

    public ExchangeOnlineEtransferEmailAliasService(
        ILogger<ExchangeOnlineEtransferEmailAliasService> logger,
        IConfiguration configuration)
    {
        _logger =
            logger;

        _provisioningMode =
            configuration["EtransferAliasProvisioningMode"]?.Trim()
            ?? "ExchangeOnline";

        _organization =
            configuration["ExchangeOnlineOrganization"]?.Trim();

        _mailboxIdentity =
            configuration["ExchangeOnlineMailboxIdentity"]?.Trim();

        _modulePath =
            configuration["ExchangeOnlineModulePath"]?.Trim();

        _appID =
            configuration["ExchangeOnlineAppID"]?.Trim();

        _certificateBase64 =
            configuration["ExchangeOnlineCertificateBase64"]?.Trim();

        _certificatePath =
            configuration["ExchangeOnlineCertificatePath"]?.Trim();

        _certificatePassword =
            configuration["ExchangeOnlineCertificatePassword"];
    }

    public async Task<bool> AliasExistsAsync(
        string emailAddress,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmailAddress =
            EtransferAliasAddress.NormalizeEmailAddress(
                emailAddress);

        if (UsesDatabaseOnlyMode())
        {
            return false;
        }

        return await ExecuteLockedAsync(
            async runspace =>
                await AliasExistsInRunspaceAsync(
                    runspace,
                    normalizedEmailAddress,
                    cancellationToken),
            cancellationToken);
    }

    public async Task AddAliasAsync(
        string emailAddress,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmailAddress =
            EtransferAliasAddress.NormalizeEmailAddress(
                emailAddress);

        if (UsesDatabaseOnlyMode())
        {
            _logger.LogInformation(
                "Database-only mode: treating {EmailAddress} as provisioned without changing Exchange Online.",
                normalizedEmailAddress);

            return;
        }

        await ExecuteLockedAsync(
            async runspace =>
            {
                if (await AliasExistsInRunspaceAsync(
                        runspace,
                        normalizedEmailAddress,
                        cancellationToken))
                {
                    throw new EtransferAliasInUseException(
                        normalizedEmailAddress);
                }

                await SetMailboxAliasAsync(
                    runspace,
                    normalizedEmailAddress,
                    addAlias: true,
                    cancellationToken);

                _logger.LogInformation(
                    "Added Exchange Online alias {EmailAddress} to mailbox {MailboxIdentity}.",
                    normalizedEmailAddress,
                    _mailboxIdentity);

                return true;
            },
            cancellationToken);
    }

    public async Task EnsureAliasAsync(
        string emailAddress,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmailAddress =
            EtransferAliasAddress.NormalizeEmailAddress(
                emailAddress);

        if (UsesDatabaseOnlyMode())
        {
            return;
        }

        await ExecuteLockedAsync(
            async runspace =>
            {
                if (!await AliasExistsInRunspaceAsync(
                        runspace,
                        normalizedEmailAddress,
                        cancellationToken))
                {
                    await SetMailboxAliasAsync(
                        runspace,
                        normalizedEmailAddress,
                        addAlias: true,
                        cancellationToken);
                }

                return true;
            },
            cancellationToken);
    }

    public async Task RemoveAliasIfExistsAsync(
        string emailAddress,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmailAddress =
            EtransferAliasAddress.NormalizeEmailAddress(
                emailAddress);

        if (UsesDatabaseOnlyMode())
        {
            _logger.LogInformation(
                "Database-only mode: treating {EmailAddress} as removed without changing Exchange Online.",
                normalizedEmailAddress);

            return;
        }

        await ExecuteLockedAsync(
            async runspace =>
            {
                if (await AliasExistsInRunspaceAsync(
                        runspace,
                        normalizedEmailAddress,
                        cancellationToken))
                {
                    await SetMailboxAliasAsync(
                        runspace,
                        normalizedEmailAddress,
                        addAlias: false,
                        cancellationToken);

                    _logger.LogInformation(
                        "Removed Exchange Online alias {EmailAddress} from mailbox {MailboxIdentity}.",
                        normalizedEmailAddress,
                        _mailboxIdentity);
                }

                return true;
            },
            cancellationToken);
    }

    private bool UsesDatabaseOnlyMode()
    {
        return string.Equals(
            _provisioningMode,
            "DatabaseOnly",
            StringComparison.OrdinalIgnoreCase);
    }

    private async Task<TResult> ExecuteLockedAsync<TResult>(
        Func<Runspace, Task<TResult>> action,
        CancellationToken cancellationToken)
    {
        ValidateConfiguration();

        await OperationLock.WaitAsync(
            cancellationToken);

        try
        {
            using var certificate =
                LoadCertificate();

            using var runspace =
                CreateRunspace();

            runspace.Open();

            await ConnectAsync(
                runspace,
                certificate,
                cancellationToken);

            try
            {
                return await action(
                    runspace);
            }
            finally
            {
                await DisconnectAsync(
                    runspace);
            }
        }
        finally
        {
            OperationLock.Release();
        }
    }

    private Runspace CreateRunspace()
    {
        EnsureBundledModulePathIsDiscoverable();

        var initialSessionState =
            InitialSessionState.CreateDefault();

        /*
         * This execution policy applies specifically to the embedded
         * PowerShell runspace hosted by the API process.
         *
         * It does not change the user's machine-wide or CurrentUser
         * execution policy.
         *
         * Bypass prevents Windows zone checks from rejecting scripts and
         * .ps1xml files copied into the Function build output.
         */
        initialSessionState.ExecutionPolicy =
            Microsoft.PowerShell.ExecutionPolicy.Bypass;

        /*
         * Exchange cmdlet functionality does not depend on PowerShell's
         * display-format data.
         *
         * Disabling format updates prevents PackageManagement and
         * PowerShellGet .format.ps1xml files from being processed while
         * the ExchangeOnlineManagement module is imported.
         */
        initialSessionState.DisableFormatUpdates =
            true;

        initialSessionState.ImportPSModule(
            new[]
            {
                ResolveModulePath()
            });

        initialSessionState.ThrowOnRunspaceOpenError =
            true;

        return RunspaceFactory.CreateRunspace(
            initialSessionState);
    }

    private static void EnsureBundledModulePathIsDiscoverable()
    {
        var bundledModulesRoot =
            Path.Combine(
                AppContext.BaseDirectory,
                "PowerShellModules");

        if (!Directory.Exists(
                bundledModulesRoot))
        {
            return;
        }

        var existingModulePath =
            Environment.GetEnvironmentVariable(
                "PSModulePath")
            ?? string.Empty;

        var modulePaths =
            existingModulePath.Split(
                Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);

        if (modulePaths.Contains(
                bundledModulesRoot,
                StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        var updatedModulePath =
            string.IsNullOrWhiteSpace(
                existingModulePath)
                ? bundledModulesRoot
                : $"{bundledModulesRoot}{Path.PathSeparator}{existingModulePath}";

        Environment.SetEnvironmentVariable(
            "PSModulePath",
            updatedModulePath);
    }

    private string ResolveModulePath()
    {
        if (!string.IsNullOrWhiteSpace(
                _modulePath))
        {
            return _modulePath;
        }

        var bundledModuleRoot =
            Path.Combine(
                AppContext.BaseDirectory,
                "PowerShellModules",
                "ExchangeOnlineManagement");

        if (Directory.Exists(
                bundledModuleRoot))
        {
            var manifest =
                Directory
                    .EnumerateFiles(
                        bundledModuleRoot,
                        "ExchangeOnlineManagement.psd1",
                        SearchOption.AllDirectories)
                    .OrderByDescending(
                        path => path,
                        StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(
                    manifest))
            {
                return manifest;
            }
        }

        return "ExchangeOnlineManagement";
    }

    private async Task ConnectAsync(
        Runspace runspace,
        X509Certificate2 certificate,
        CancellationToken cancellationToken)
    {
        using var powerShell =
            PowerShell.Create();

        powerShell.Runspace =
            runspace;

        powerShell
            .AddCommand(
                "Connect-ExchangeOnline")
            .AddParameter(
                "AppId",
                _appID)
            .AddParameter(
                "Organization",
                _organization)
            .AddParameter(
                "Certificate",
                certificate)
            .AddParameter(
                "ShowBanner",
                false)
            .AddParameter(
                "SkipLoadingFormatData")
            .AddParameter(
                "CommandName",
                new[]
                {
                    "Get-EXORecipient",
                    "Set-Mailbox"
                });

        await InvokeAsync(
            powerShell,
            "Connect-ExchangeOnline",
            cancellationToken);
    }

    private static async Task DisconnectAsync(
        Runspace runspace)
    {
        try
        {
            using var powerShell =
                PowerShell.Create();

            powerShell.Runspace =
                runspace;

            powerShell
                .AddCommand(
                    "Disconnect-ExchangeOnline")
                .AddParameter(
                    "Confirm",
                    false);

            await InvokeAsync(
                powerShell,
                "Disconnect-ExchangeOnline",
                CancellationToken.None);
        }
        catch
        {
            /*
             * The runspace is disposed immediately afterward.
             * A disconnect error must not hide the alias operation result.
             */
        }
    }

    private static async Task<bool> AliasExistsInRunspaceAsync(
        Runspace runspace,
        string emailAddress,
        CancellationToken cancellationToken)
    {
        using var powerShell =
            PowerShell.Create();

        powerShell.Runspace =
            runspace;

        powerShell
            .AddCommand(
                "Get-EXORecipient")
            .AddParameter(
                "Filter",
                $"EmailAddresses -eq 'smtp:{emailAddress}'")
            .AddParameter(
                "ResultSize",
                1);

        var results =
            await InvokeAsync(
                powerShell,
                "Get-EXORecipient",
                cancellationToken);

        return results.Count > 0;
    }

    private async Task SetMailboxAliasAsync(
        Runspace runspace,
        string emailAddress,
        bool addAlias,
        CancellationToken cancellationToken)
    {
        using var powerShell =
            PowerShell.Create();

        powerShell.Runspace =
            runspace;

        var changes =
            new Hashtable(
                StringComparer.OrdinalIgnoreCase)
            {
                [addAlias
                    ? "Add"
                    : "Remove"] =
                        emailAddress
            };

        powerShell
            .AddCommand(
                "Set-Mailbox")
            .AddParameter(
                "Identity",
                _mailboxIdentity)
            .AddParameter(
                "EmailAddresses",
                changes);

        await InvokeAsync(
            powerShell,
            addAlias
                ? "Set-Mailbox add alias"
                : "Set-Mailbox remove alias",
            cancellationToken);
    }

    private static async Task<
        System.Collections.ObjectModel.Collection<PSObject>>
        InvokeAsync(
            PowerShell powerShell,
            string operationName,
            CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var registration =
            cancellationToken.Register(
                () =>
                {
                    try
                    {
                        powerShell.Stop();
                    }
                    catch
                    {
                        /*
                         * Best-effort cancellation only.
                         */
                    }
                });

        var results =
            await Task.Run(
                () => powerShell.Invoke(),
                cancellationToken);

        if (powerShell.HadErrors)
        {
            var errors =
                string.Join(
                    Environment.NewLine,
                    powerShell.Streams.Error.Select(
                        error => error.ToString()));

            throw new InvalidOperationException(
                $"Exchange Online operation '{operationName}' failed: {errors}");
        }

        return results;
    }

    private X509Certificate2 LoadCertificate()
    {
        byte[] certificateBytes;

        if (!string.IsNullOrWhiteSpace(
                _certificateBase64))
        {
            try
            {
                certificateBytes =
                    Convert.FromBase64String(
                        _certificateBase64);
            }
            catch (FormatException exception)
            {
                throw new InvalidOperationException(
                    "ExchangeOnlineCertificateBase64 is not valid base64.",
                    exception);
            }
        }
        else if (!string.IsNullOrWhiteSpace(
                     _certificatePath))
        {
            certificateBytes =
                File.ReadAllBytes(
                    _certificatePath);
        }
        else
        {
            throw new InvalidOperationException(
                "Configure ExchangeOnlineCertificateBase64 or ExchangeOnlineCertificatePath.");
        }

        return new X509Certificate2(
            certificateBytes,
            _certificatePassword,
            X509KeyStorageFlags.EphemeralKeySet |
            X509KeyStorageFlags.Exportable);
    }

    private void ValidateConfiguration()
    {
        if (UsesDatabaseOnlyMode())
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(
                _organization))
        {
            throw new InvalidOperationException(
                "Missing ExchangeOnlineOrganization configuration.");
        }

        if (string.IsNullOrWhiteSpace(
                _mailboxIdentity))
        {
            throw new InvalidOperationException(
                "Missing ExchangeOnlineMailboxIdentity configuration.");
        }

        if (string.IsNullOrWhiteSpace(
                _appID))
        {
            throw new InvalidOperationException(
                "Missing ExchangeOnlineAppID configuration.");
        }

        if (string.IsNullOrWhiteSpace(
                _certificateBase64) &&
            string.IsNullOrWhiteSpace(
                _certificatePath))
        {
            throw new InvalidOperationException(
                "Configure ExchangeOnlineCertificateBase64 or ExchangeOnlineCertificatePath.");
        }
    }
}