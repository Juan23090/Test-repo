using EMA.Api.Services.Sandbox;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Sandbox;

public sealed class SandboxCleanupFunction
{
    private readonly ISandboxService _sandboxService;
    private readonly ILogger<SandboxCleanupFunction> _logger;

    public SandboxCleanupFunction(
        ISandboxService sandboxService,
        ILogger<SandboxCleanupFunction> logger)
    {
        _sandboxService =
            sandboxService;

        _logger =
            logger;
    }

    [Function("DeleteExpiredSandboxes")]
    public async Task RunAsync(
        [TimerTrigger("0 0 0 1 * *")]
        TimerInfo timer,
        CancellationToken cancellationToken)
    {
        var result =
            await _sandboxService
                .DeleteExpiredSandboxesAsync(
                    cancellationToken:
                        cancellationToken);

        _logger.LogInformation(
            "Monthly sandbox cleanup finished. Deleted {DeletedSandboxCount} sandbox organization(s).",
            result.DeletedSandboxCount);
    }
}