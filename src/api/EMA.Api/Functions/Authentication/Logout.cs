using EMA.Api.Services.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace EMA.Api.Functions.Authentication;

public sealed class Logout
{
    private readonly IApiSessionService
        _sessionService;

    public Logout(
        IApiSessionService sessionService)
    {
        _sessionService =
            sessionService;
    }

    [Function("Logout")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,             "post",
            Route = "auth/logout")]
        HttpRequest request)
    {
        var identity =
            request.GetRequiredApiIdentity();

        await _sessionService
            .RevokeSessionAsync(
                identity,
                request.HttpContext
                    .RequestAborted);

        return new NoContentResult();
    }
}
