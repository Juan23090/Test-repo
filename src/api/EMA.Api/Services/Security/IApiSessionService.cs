using EMA.Api.DTOs;

namespace EMA.Api.Services.Security;

public interface IApiSessionService
{
    Task<ApiAccessToken>
        CreateSessionAsync(
            LoginResponseDto loginResponse,
            CancellationToken cancellationToken = default);

    Task<bool> IsSessionActiveAsync(
        ApiRequestIdentity identity,
        CancellationToken cancellationToken = default);

    Task RevokeSessionAsync(
        ApiRequestIdentity identity,
        CancellationToken cancellationToken = default);
}
