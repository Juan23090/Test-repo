namespace EMA.Api.Services.Security;

public interface IScreenFeedbackAuthorizationService
{
    Task<bool> CanAccessMenuItemAsync(
        ApiRequestIdentity identity,
        int menuItemID,
        CancellationToken cancellationToken = default);

    Task<bool> CanAccessEnhancementRequestAsync(
        ApiRequestIdentity identity,
        int enhancementRequestID,
        CancellationToken cancellationToken = default);

    Task<bool> CanAccessInsiderRouteAsync(
        ApiRequestIdentity identity,
        string route,
        CancellationToken cancellationToken = default);
}
