using Xunit;

namespace EMA.App.Tests.Navigation;

public sealed class Task42ScreenLevelIconVisibilityTests
{
    [Fact]
    public void ScreenLevelViewerIconsAreOmittedWhenTheirCountsAreZero()
    {
        var source = ReadSource(
            "src", "app", "EMA.App", "Components", "ScreenLevelActions.razor");

        Assert.Contains("@if (_summary.Faqs.Count > 0)", source);
        Assert.Contains("@if (_summary.Enhancements.Count > 0)", source);
        Assert.Contains("@if (_summary.SupportTickets.Count > 0)", source);
    }

    private static string ReadSource(params string[] segments)
    {
        var path = segments.Aggregate(FindRepositoryRoot(), Path.Combine);
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "app", "EMA.App")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the EMA repository root.");
    }
}
