using EMA.Api.DTOs;
using Xunit;

namespace EMA.Api.Tests.Banks;

public sealed class BankMappingHelperTests
{
    // make sure Y, y, or Y with spaces converts to true
    [Theory]
    [InlineData("Y")]
    [InlineData("y")]
    [InlineData(" Y ")]
    public void ToBoolReturnsTrueForValidYStrings(string? value)
    {
        var result = BankMappingHelper.ToBool(value);
        Assert.True(result);
    }

    // make sure N, empty, null, or any other random string converts to false
    [Theory]
    [InlineData("N")]
    [InlineData("n")]
    [InlineData(" N ")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("random")]
    public void ToBoolReturnsFalseForNonYStrings(string? value)
    {
        var result = BankMappingHelper.ToBool(value);
        Assert.False(result);
    }

    // check that true maps to Y and false maps to N for database char columns
    [Theory]
    [InlineData(true, "Y")]
    [InlineData(false, "N")]
    public void ToActiveCharReturnsExpectedChar(bool isActive, string expected)
    {
        var result = BankMappingHelper.ToActiveChar(isActive);
        Assert.Equal(expected, result);
    }

    // check that whitespace gets trimmed off and null turns into an empty string
    [Theory]
    [InlineData(" TD ", "TD")]
    [InlineData("RBC", "RBC")]
    [InlineData("   ", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void TrimOrEmptyCleansUpStringProperly(string? input, string expected)
    {
        var result = BankMappingHelper.TrimOrEmpty(input);
        Assert.Equal(expected, result);
    }

    // null or whitespace should pass since instructions url is optional
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsValidInstructionsUrlAllowsNullOrWhitespace(string? input)
    {
        var result = BankMappingHelper.IsValidInstructionsUrl(input);
        Assert.True(result);
    }

    // valid absolute https urls should be allowed
    [Theory]
    [InlineData("https://example.com")]
    [InlineData("https://www.rbcroyalbank.com/ways-to-bank/tutorials/statements.html")]
    [InlineData("https://atb.com/support?topic=bank&view=pdf#download")]
    public void IsValidInstructionsUrlAllowsValidHttpsUrls(string url)
    {
        var result = BankMappingHelper.IsValidInstructionsUrl(url);
        Assert.True(result);
    }

    // unsafe schemes or non-https urls must be rejected
    [Theory]
    [InlineData("http://example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("javascript:void(0)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://files.example.com")]
    [InlineData("example.com")]
    [InlineData("/relative/url/path")]
    public void IsValidInstructionsUrlRejectsUnsafeOrNonHttpsSchemes(string url)
    {
        var result = BankMappingHelper.IsValidInstructionsUrl(url);
        Assert.False(result);
    }

    // urls longer than 256 chars should be rejected
    [Fact]
    public void IsValidInstructionsUrlRejectsUrlsOver256Chars()
    {
        var longUrl = "https://example.com/" + new string('a', 250);
        var result = BankMappingHelper.IsValidInstructionsUrl(longUrl);
        Assert.False(result);
    }
}
