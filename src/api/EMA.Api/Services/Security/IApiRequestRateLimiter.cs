using Microsoft.AspNetCore.Http;

namespace EMA.Api.Services.Security;

public interface IApiRequestRateLimiter
{
    Task<ApiRateLimitResult> CheckAsync(
        string functionName,
        HttpRequest request,
        ApiRequestIdentity? identity,
        CancellationToken cancellationToken = default);
}

public sealed record ApiRateLimitResult(
    bool IsAllowed,
    TimeSpan RetryAfter)
{
    public static ApiRateLimitResult Allowed() =>
        new(
            true,
            TimeSpan.Zero);

    public static ApiRateLimitResult Rejected(
        TimeSpan retryAfter) =>
        new(
            false,
            retryAfter);
}
