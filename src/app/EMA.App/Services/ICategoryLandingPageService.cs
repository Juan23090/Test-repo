using EMA.App.Models;

namespace EMA.App.Services;

public interface ICategoryLandingPageService
{
    Task<CategoryLandingPageResult> GetLandingPageAsync(
        int EMAOrganizationID,
        int userID,
        int categoryID,
        CancellationToken cancellationToken = default);

    Task<string> GetLandingUrlAsync(
        int EMAOrganizationID,
        int userID,
        int categoryID,
        CancellationToken cancellationToken = default);

    Task<RouteAccessResult> CheckRouteAccessAsync(
        int EMAOrganizationID,
        int userID,
        string path,
        CancellationToken cancellationToken = default);

    Task<bool> IsPublicRouteAsync(
        string path,
        CancellationToken cancellationToken = default);

    Task<CategoryLandingPageResult> GetDonorLandingPageAsync(
        int EMAOrganizationID,
        int donorID,
        int categoryID,
        CancellationToken cancellationToken = default);

    Task<string> GetDonorLandingUrlAsync(
        int EMAOrganizationID,
        int donorID,
        int categoryID,
        CancellationToken cancellationToken = default);

    Task<RouteAccessResult> CheckDonorRouteAccessAsync(
        int EMAOrganizationID,
        int donorID,
        string path,
        CancellationToken cancellationToken = default);
}
