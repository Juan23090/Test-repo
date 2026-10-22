using Microsoft.AspNetCore.Http;

namespace EMA.Api.Services.Authentication;

public interface ITrustedDeviceService
{
    Task<bool> IsTrustedAsync(
        string emailAddress,
        HttpRequest request,
        CancellationToken cancellationToken = default);

    Task RegisterSuccessfulAuthenticationAsync(
        string emailAddress,
        HttpRequest request,
        CancellationToken cancellationToken = default);
}
