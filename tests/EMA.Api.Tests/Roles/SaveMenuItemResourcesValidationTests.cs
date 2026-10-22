using System.Reflection;
using EMA.Api.DTOs;
using EMA.Api.Functions.Roles;

namespace EMA.Api.Tests.Roles;

public sealed class SaveMenuItemResourcesValidationTests
{
    [Fact]
    public void VideoWithoutBlobIdIsRejected()
    {
        var resource = Resource("V");
        var result = Normalize(resource);
        Assert.False(result.Success);
        Assert.Contains("Video resources require", result.Message);
    }

    [Fact]
    public void AudioWithoutBlobOrScriptIsRejected()
    {
        var resource = Resource("A");
        var result = Normalize(resource);
        Assert.False(result.Success);
        Assert.Contains("Audio resources require", result.Message);
    }

    [Theory]
    [InlineData("https://example.com/setup")]
    [InlineData("//example.com/setup")]
    public void ExternalSetupUrlsAreRejected(string url)
    {
        var resource = Resource("W");
        resource.ScreenURL = url;
        var result = Normalize(resource, "settings");
        Assert.False(result.Success);
        Assert.Contains("internal setup Screen URL", result.Message);
    }

    [Theory]
    [InlineData("folder/blob")]
    [InlineData("folder\\blob")]
    [InlineData("https://example.com/blob")]
    public void InvalidBlobPathsAreRejected(string blob)
    {
        var resource = Resource("V");
        resource.AudioVideoBlob = blob;
        var result = Normalize(resource);
        Assert.False(result.Success);
        Assert.Contains("valid EMA blob ID", result.Message);
    }

    [Fact]
    public void ValidResourceIsNormalizedForPersistence()
    {
        var resource = Resource("a");
        resource.AudioScript = "  Welcome to EMA  ";
        var result = Normalize(resource);
        Assert.True(result.Success);
        Assert.NotNull(result.Resource);
        Assert.Equal("A", result.Resource!.MenuItemResourceTypeID);
        Assert.Equal("Welcome to EMA", result.Resource.AudioScript);
        Assert.Equal(42, result.Resource.MenuItemID);
    }

    [Fact]
    public void WrenchResourceUsesConventionalInternalSetupUrl()
    {
        var resource = Resource("W");
        var result = Normalize(resource, "menu-items");
        Assert.True(result.Success);
        Assert.Equal("/menu-items-setup", result.Resource!.ScreenURL);
    }

    private static MenuItemResourceDto Resource(string type) =>
        new()
        {
            MenuItemResourceTypeID = type,
            Sequence = 1,
            Description = "Resource"
        };

    private static NormalizeResult Normalize(
        MenuItemResourceDto resource,
        string? menuUrl = "menu-items")
    {
        var method = typeof(SaveMenuItemResources).GetMethod(
            "TryNormalizeResource",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        object?[] arguments = [42, menuUrl, resource, null, null];
        var success = (bool)method!.Invoke(null, arguments)!;

        return new NormalizeResult(
            success,
            arguments[3] as MenuItemResourceDto,
            arguments[4] as string);
    }

    private sealed record NormalizeResult(
        bool Success,
        MenuItemResourceDto? Resource,
        string? Message);
}
