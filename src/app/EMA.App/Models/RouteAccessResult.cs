namespace EMA.App.Models;

public sealed class RouteAccessResult
{
    public bool IsAllowed { get; set; }

    public string Reason { get; set; } =
        string.Empty;

    public int? MenuItemID { get; set; }

    public int? CategoryID { get; set; }

    public string Url { get; set; } =
        string.Empty;

    public bool IsConfiguredLandingPage { get; set; }
}