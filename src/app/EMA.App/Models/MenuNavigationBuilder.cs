using EMA.App.Models;

namespace EMA.App.Services;

/// <summary>
/// Converts the flat MenuItem result set into header/child navigation groups.
/// A detail item belongs to the nearest preceding header in MenuItemID order.
/// Detail rows that precede every header are attached to the first header so a
/// legacy/reserved ID cannot make an authorized link invisible.
/// </summary>
public static class MenuNavigationBuilder
{
    public static IReadOnlyList<MenuNavigationGroup> BuildGroups(
        IEnumerable<MenuItemDto> menuItems,
        int categoryID)
    {
        ArgumentNullException.ThrowIfNull(menuItems);

        var groups = new List<MenuNavigationGroup>();
        var leadingChildren = new List<MenuItemDto>();
        MenuNavigationGroup? currentGroup = null;

        var activeItems = menuItems
            .Where(item =>
                item.IsActive &&
                item.MenuItemCategoryID == categoryID)
            .OrderBy(item => item.MenuItemID);

        foreach (var item in activeItems)
        {
            if (item.MenuItemRowID == MenuNavigationConstants.HeaderRowID)
            {
                currentGroup = new MenuNavigationGroup
                {
                    Header = item
                };

                groups.Add(currentGroup);

                if (groups.Count == 1 && leadingChildren.Count > 0)
                {
                    currentGroup.Children.AddRange(leadingChildren);
                }

                continue;
            }

            if (item.MenuItemRowID != MenuNavigationConstants.DetailRowID)
            {
                continue;
            }

            if (currentGroup is null)
            {
                leadingChildren.Add(item);
            }
            else
            {
                currentGroup.Children.Add(item);
            }
        }

        return groups;
    }
}
