using EMA.Api.Services.Security;
using Microsoft.AspNetCore.Http;

namespace EMA.Api.Tests.Security;

public sealed class SwaManagedAuthCompatibilityTests
{
    [Theory]
    [InlineData("orange-cliff-07351c91e.7.azurestaticapps.net")]
    [InlineData("example.azurestaticapps.net")]
    [InlineData("AZURESTATICAPPS.NET")]
    public void StaticWebAppsHostsEnableCompatibilityWithoutExternalSetting(
        string host)
    {
        var context =
            new DefaultHttpContext();

        context.Request.Host =
            new HostString(host);

        Assert.True(
            ApiSecurityMiddleware
                .ShouldUseSwaManagedAuthCompatibility(
                    context.Request,
                    configuredCompatibilityEnabled: false));
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("api.emailmoney.app")]
    [InlineData("azurestaticapps.net.example.com")]
    public void NonStaticWebAppsHostsRemainBearerOnlyByDefault(
        string host)
    {
        var context =
            new DefaultHttpContext();

        context.Request.Host =
            new HostString(host);

        Assert.False(
            ApiSecurityMiddleware
                .ShouldUseSwaManagedAuthCompatibility(
                    context.Request,
                    configuredCompatibilityEnabled: false));
    }


    [Fact]
    public void RawForwardedStaticWebAppsHostDoesNotEnableCompatibility()
    {
        var context =
            new DefaultHttpContext();

        context.Request.Host =
            new HostString("api.emailmoney.app");
        context.Request.Headers["X-Forwarded-Host"] =
            "orange-cliff-07351c91e.7.azurestaticapps.net";

        Assert.False(
            ApiSecurityMiddleware
                .ShouldUseSwaManagedAuthCompatibility(
                    context.Request,
                    configuredCompatibilityEnabled: false));
    }

    [Fact]
    public void ExplicitCompatibilitySettingRemainsSupported()
    {
        var context =
            new DefaultHttpContext();

        context.Request.Host =
            new HostString("api.emailmoney.app");

        Assert.True(
            ApiSecurityMiddleware
                .ShouldUseSwaManagedAuthCompatibility(
                    context.Request,
                    configuredCompatibilityEnabled: true));
    }
}
