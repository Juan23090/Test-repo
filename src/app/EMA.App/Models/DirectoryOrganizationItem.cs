namespace EMA.App.Models;

/// <summary>
/// Represents a participating non-profit or community organization in the public opt-in directory.
/// </summary>
public sealed class DirectoryOrganizationItem
{
    public int OrganizationID { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string? CustomClass { get; set; }
    public string? Category { get; set; }
    public string? Location { get; set; }
    public string? WebsiteUrl { get; set; }
    public bool IsOptedIn { get; set; } = true;
}
