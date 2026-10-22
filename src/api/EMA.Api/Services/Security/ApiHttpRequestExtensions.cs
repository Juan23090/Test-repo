using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker.Http;

namespace EMA.Api.Services.Security;

public static class ApiHttpRequestExtensions
{
    public static ApiRequestIdentity
        GetRequiredApiIdentity(
            this HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(
            request);

        if (!ApiRequestIdentity
                .TryFromPrincipal(
                    request.HttpContext.User,
                    out var identity) ||
            identity is null)
        {
            throw new UnauthorizedAccessException(
                "A validated API identity is required.");
        }

        return identity;
    }

    public static ApiRequestIdentity
        GetRequiredApiIdentity(
            this HttpRequestData request)
    {
        ArgumentNullException.ThrowIfNull(
            request);

        if (!request.FunctionContext.Items
                .TryGetValue(
                    typeof(ApiRequestIdentity),
                    out var value) ||
            value is not ApiRequestIdentity
                identity)
        {
            throw new UnauthorizedAccessException(
                "A validated API identity is required.");
        }

        return identity;
    }
}
