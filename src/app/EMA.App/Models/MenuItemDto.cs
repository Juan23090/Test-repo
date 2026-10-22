using System.Text.Json.Serialization;

namespace EMA.App.Models;

public sealed class MenuItemDto
{
    public int MenuItemID { get; set; }

    [JsonPropertyName("originalMenuItemID")]
    public int? OriginalMenuItemID { get; set; }

    [JsonPropertyName("roleIDs")]
    public List<int> RoleIDs { get; set; } = new();

    public int? MenuItemCategoryID { get; set; }

    public int? Row { get; set; }

    public int MenuItemTypeID { get; set; }

    public string DisplayControlID { get; set; } = "M";

    public string MenuName { get; set; } = string.Empty;

    public string ScreenTitle { get; set; } = string.Empty;

    public string? URL { get; set; }

    public List<MenuItemResourceDto> Resources { get; set; } = new();

    [JsonPropertyName("isActive")]
    public string? IsActiveValue { get; set; }

    [JsonIgnore]
    public int MenuItemRowID
    {
        get => Row ?? 0;
        set => Row = value;
    }

    [JsonIgnore]
    public bool IsActive
    {
        get =>
            string.Equals(IsActiveValue, "Y", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(IsActiveValue, "YES", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(IsActiveValue, "TRUE", StringComparison.OrdinalIgnoreCase) ||
            IsActiveValue == "1";

        set => IsActiveValue = value ? "Y" : "N";
    }
}
