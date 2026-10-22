using EMA.App.Models;

namespace EMA.App.Services;

public interface IMenuNavigationService
{
    Task<IReadOnlyList<MenuItemDto>> GetMenuItemsAsync(
        int EMAOrganizationID,
        int userID,
        int categoryID,
        bool isDonorSession = false,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MenuItemDto>> GetPublicMenuItemsAsync(
        CancellationToken cancellationToken = default);

    Task LogMenuItemUsageAsync(
        int menuItemID,
        CancellationToken cancellationToken = default);
}