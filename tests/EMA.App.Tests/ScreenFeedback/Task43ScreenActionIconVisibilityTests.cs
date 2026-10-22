using Xunit;

namespace EMA.App.Tests.ScreenFeedback;

public sealed class Task43ScreenActionIconVisibilityTests
{
    [Fact]
    public void ViewerIconsAreNotRenderedWhenTheirCountsAreZero()
    {
        var source = File.ReadAllText(
            FindRepositoryFile(
                "src", "app", "EMA.App", "Components", "ScreenLevelActions.razor"));

        Assert.Contains("CanUseScreenActions && HasVisibleScreenActions", source);
        Assert.Contains("@if (_summary.Faqs.Count > 0)", source);
        Assert.Contains("@if (_summary.Enhancements.Count > 0)", source);
        Assert.Contains("@if (_summary.SupportTickets.Count > 0)", source);

        Assert.DoesNotContain("disabled=\"@(_summary.Faqs.Count == 0)\"", source);
        Assert.DoesNotContain("disabled=\"@(_summary.Enhancements.Count == 0)\"", source);
        Assert.DoesNotContain("disabled=\"@(_summary.SupportTickets.Count == 0)\"", source);
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(
                new[] { directory.FullName }.Concat(segments).ToArray());

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the EMA repository root.");
    }
}
