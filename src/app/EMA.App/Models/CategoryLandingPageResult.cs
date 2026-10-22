namespace EMA.App.Models;

public sealed class CategoryLandingPageResult
{
    public int CategoryID { get; set; }

    public string CategoryName { get; set; } =
        string.Empty;

    public int? LandingPageMenuItemID { get; set; }

    public string MenuName { get; set; } =
        string.Empty;

    public string ScreenTitle { get; set; } =
        string.Empty;

    public string Url { get; set; } =
        string.Empty;

    public bool IsConfiguredLandingPage { get; set; }

    public bool IsFallback { get; set; }
}