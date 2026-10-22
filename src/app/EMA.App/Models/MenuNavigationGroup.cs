namespace EMA.App.Models;

/// <summary>
/// Represents one header menu item and the detail items that belong under it.
/// </summary>
public sealed class MenuNavigationGroup
{
    public MenuItemDto Header { get; init; } = new();

    public List<MenuItemDto> Children { get; } = new();
}