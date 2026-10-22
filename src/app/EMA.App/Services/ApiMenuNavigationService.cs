using System.Net.Http.Json;
using EMA.App.Models;

namespace EMA.App.Services;

/// <summary>
/// Loads the menu items that the API has authorized for a user or donor.
/// </summary>
public sealed class ApiMenuNavigationService : IMenuNavigationService
{
    private readonly HttpClient _http;
    private readonly IOrganizationService _organizationService;
    private readonly ILogger<ApiMenuNavigationService> _logger;

    public ApiMenuNavigationService(
        HttpClient http,
        IOrganizationService organizationService,
        ILogger<ApiMenuNavigationService> logger)
    {
        _http = http;
        _organizationService = organizationService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MenuItemDto>> GetMenuItemsAsync(
        int EMAOrganizationID,
        int userID,
        int categoryID,
        bool isDonorSession = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var menuItems =
                await _organizationService.GetMenuItemsByUserAsync(
                    EMAOrganizationID,
                    userID,
                    categoryID,
                    isDonorSession,
                    cancellationToken);

            if (menuItems is null)
            {
                _logger.LogWarning(
                    "No menu data returned for organization {EMAOrganizationID}, user/donor {UserID}, category {CategoryID}.",
                    EMAOrganizationID,
                    userID,
                    categoryID);

                return Array.Empty<MenuItemDto>();
            }

            return menuItems
                .Where(item =>
                    item.IsActive &&
                    item.MenuItemCategoryID == categoryID)
                .OrderBy(item => item.MenuItemID)
                .ToList();
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Unable to load navigation for organization {EMAOrganizationID}, user/donor {UserID}, category {CategoryID}.",
                EMAOrganizationID,
                userID,
                categoryID);

            return Array.Empty<MenuItemDto>();
        }
    }
    public async Task<IReadOnlyList<MenuItemDto>> GetPublicMenuItemsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var menuItems =
                await _http
                    .GetFromJsonAsync<List<MenuItemDto>>(
                        "/api/navigation/public-menu-items",
                        cancellationToken);

            if (menuItems is null)
            {
                _logger.LogWarning(
                    "No public brochure menu data was returned.");

                return Array.Empty<MenuItemDto>();
            }

            return menuItems
                .Where(item =>
                    item.IsActive &&
                    item.MenuItemCategoryID ==
                    MenuNavigationConstants
                        .BrochureCategoryID)
                .OrderBy(item => item.MenuItemID)
                .ToList();
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Unable to load public brochure navigation.");

            return Array.Empty<MenuItemDto>();
        }
    }

    public async Task LogMenuItemUsageAsync(
        int menuItemID,
        CancellationToken cancellationToken = default)
    {
        if (menuItemID <= 0)
        {
            return;
        }

        var logged =
            await _organizationService
                .LogMenuItemUsageAsync(
                    menuItemID,
                    cancellationToken);

        if (!logged)
        {
            _logger.LogWarning(
                "Menu item usage could not be logged for menu item {MenuItemID}.",
                menuItemID);
        }
    }

}