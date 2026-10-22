using EMA.App.Models;

namespace EMA.App.Services;

public interface IOrganizationService
{
    Task<OrganizationSummary?> GetOrganizationAsync(
        int orgID,
        CancellationToken cancellationToken = default);

    Task<List<OrganizationAuditObject>?> GetOrganizationAuditsAsync(
        int orgID,
        CancellationToken cancellationToken = default);

    Task<bool> PostOrganizationAuditAsync(
        int orgID,
        OrganizationSummary payload,
        CancellationToken cancellationToken = default);

    Task<bool> UpdateOrganizationAsync(
        int orgID,
        OrganizationSummary organization,
        CancellationToken cancellationToken = default);

    Task<OrganizationUpdateResult> UpdateOrganizationWithResultAsync(
        int orgID,
        OrganizationSummary organization,
        CancellationToken cancellationToken = default);

    Task<EtransferAliasAvailabilityResult> CheckEtransferAliasAvailabilityAsync(
        int orgID,
        string prefix,
        CancellationToken cancellationToken = default);

    Task<JeSettings?> GetJeSettingsAsync(
        int orgID,
        CancellationToken cancellationToken = default);

    Task<bool> UpdateJeSettingsAsync(
        int orgID,
        string jeSummarizeBy,
        string jeByDate,
        CancellationToken cancellationToken = default);

    Task<bool> CreateOrganizationAsync(
        OrganizationSummary organization,
        CancellationToken cancellationToken = default);

    Task<List<UserItem>?> GetUsersAsync(
        int orgID,
        CancellationToken cancellationToken = default);

    Task<bool> SaveUserAsync(
        int orgID,
        UserItem user,
        CancellationToken cancellationToken = default);

    Task<List<RoleItem>?> GetRolesAsync(
        CancellationToken cancellationToken = default);

    Task<List<MenuItemDto>?> GetMenuItemsByUserAsync(
        int orgID,
        int userID,
        int? categoryID = null,
        bool isDonorSession = false,
        CancellationToken cancellationToken = default);

    Task<List<MenuItemDto>?> GetMenuItemsAsync(
        CancellationToken cancellationToken = default);

    Task<List<LookupItem>?> GetMenuItemCategoriesAsync(
        CancellationToken cancellationToken = default);

    Task<List<LookupItem>?> GetMenuItemRowsAsync(
        CancellationToken cancellationToken = default);

    Task<List<LookupItem>?> GetMenuItemTypesAsync(
        CancellationToken cancellationToken = default);

    Task<List<MenuItemResourceTypeDto>?> GetMenuItemResourceTypesAsync(
        CancellationToken cancellationToken = default);

    Task<bool> SaveMenuItemAsync(
        MenuItemDto menuItem,
        CancellationToken cancellationToken = default);

    Task<List<MenuItemResourceDto>?> SaveMenuItemResourcesAsync(
        int menuItemID,
        IReadOnlyCollection<MenuItemResourceDto> resources,
        CancellationToken cancellationToken = default);

    Task<MenuItemAudioGenerationResult> GenerateMenuItemAudioAsync(
        int menuItemID,
        int menuItemRowID,
        CancellationToken cancellationToken = default);

    Task<bool> LogMenuItemUsageAsync(
        int menuItemID,
        CancellationToken cancellationToken = default);
}