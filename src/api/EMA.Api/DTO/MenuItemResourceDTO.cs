namespace EMA.Api.DTOs;

public sealed class MenuItemResourceDto
{
    public int MenuItemRowID { get; set; }
    public int MenuItemID { get; set; }
    public string MenuItemResourceTypeID { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? AudioScript { get; set; }
    public string? AudioVideoBlob { get; set; }
    public string? ScreenURL { get; set; }
    public string ResourceTypeDescription { get; set; } = string.Empty;
    public string ResourceTypeName { get; set; } = string.Empty;
    public string? RawSVG { get; set; }
}

public sealed class MenuItemResourceTypeDto
{
    public string MenuItemResourceTypeID { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string RawSVG { get; set; } = string.Empty;
}
