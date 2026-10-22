using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;

namespace EMA.Api.Functions;

public sealed class OptionsPreflight
{
    [Function(nameof(OptionsPreflight))]
    public void Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "options",
            Route = "{*route}")]
        HttpRequest request)
    {
        // ApiSecurityMiddleware validates the Origin and completes preflight requests.
    }
}
