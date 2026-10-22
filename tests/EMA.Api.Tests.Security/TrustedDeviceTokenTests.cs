using EMA.Api.Services.Authentication;

namespace EMA.Api.Tests.Security;

public sealed class TrustedDeviceTokenTests
{
    [Fact]
    public void CreateReturnsOpaqueUrlSafeTokens()
    {
        var first =
            TrustedDeviceToken.Create();

        var second =
            TrustedDeviceToken.Create();

        Assert.Equal(
            43,
            first.Length);

        Assert.True(
            TrustedDeviceToken.IsWellFormed(
                first));

        Assert.True(
            TrustedDeviceToken.IsWellFormed(
                second));

        Assert.NotEqual(
            first,
            second);

        Assert.DoesNotContain(
            "=",
            first);

        Assert.DoesNotContain(
            "+",
            first);

        Assert.DoesNotContain(
            "/",
            first);
    }

    [Fact]
    public void HashIsDeterministicAndSha256Length()
    {
        var token =
            TrustedDeviceToken.Create();

        var first =
            TrustedDeviceToken.Hash(
                token);

        var second =
            TrustedDeviceToken.Hash(
                token);

        Assert.Equal(
            32,
            first.Length);

        Assert.Equal(
            first,
            second);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!")]
    public void InvalidTokensAreRejected(
        string token)
    {
        Assert.False(
            TrustedDeviceToken.IsWellFormed(
                token));

        Assert.Throws<ArgumentException>(
            () =>
                TrustedDeviceToken.Hash(
                    token));
    }
}
