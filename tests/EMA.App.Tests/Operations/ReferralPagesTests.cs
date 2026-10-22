using System.Reflection;
using EMA.App.Components.Pages.Operations;
using EMA.App.Models;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace EMA.App.Tests.Operations;

public sealed class ReferralPagesTests
{
    private static readonly string[] ReferralPageFiles =
    {
        "SendReferrals.razor",
        "AcceptReferral.razor",
        "ReferralDetails.razor",
        "ReferralCoverPage.razor"
    };

    [Theory]
    [InlineData(typeof(SendReferrals), "/send-referrals")]
    [InlineData(typeof(SendReferrals), "/organization/send-referrals")]
    [InlineData(typeof(AcceptReferral), "/accept-referral")]
    [InlineData(typeof(AcceptReferral), "/organization/accept-referral")]
    [InlineData(typeof(ReferralDetails), "/referral-details")]
    [InlineData(typeof(ReferralDetails), "/organization/referral-details")]
    [InlineData(typeof(ReferralCoverPage), "/referral-program")]
    [InlineData(typeof(ReferralCoverPage), "/organization/referral-program")]
    public void ReferralPagesDeclareExpectedRoutes(
        Type pageType,
        string expectedRoute)
    {
        var routes =
            pageType.GetCustomAttributes<RouteAttribute>();

        Assert.Contains(
            routes,
            route => route.Template == expectedRoute);
    }

    [Fact]
    public void ReferralPagesDoNotRenderMarkupString()
    {
        // scripts/security_gate.py fails the build on MarkupString, so a page
        // that reintroduces it breaks CI rather than only shipping an XSS sink.
        foreach (var file in ReferralPageFiles)
        {
            Assert.DoesNotContain("MarkupString", ReadPageSource(file), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ReferralPagesResolveTheOrganizationFromTheSessionWithoutDefaultingToAnotherTenant()
    {
        // "?? 1" would silently address the platform organization when the
        // session is missing; the repo convention is "?? 0" plus a guard.
        foreach (var file in ReferralPageFiles)
        {
            var source = ReadPageSource(file);

            Assert.DoesNotContain("EMAOrganizationID ?? 1", source, StringComparison.Ordinal);
            Assert.Contains("EMAOrganizationID ?? 0", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void BothReferralHistoryScreensRenderTheSharedTable()
    {
        foreach (var file in new[] { "SendReferrals.razor", "ReferralDetails.razor" })
        {
            var source = ReadPageSource(file);

            Assert.Contains("<ReferralHistoryTable", source, StringComparison.Ordinal);
            Assert.DoesNotContain("history-table\">", source, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("office@hopecommunity.ca", true)]
    [InlineData("Pastor.Smith@community-church.org", true)]
    [InlineData("not-an-email", false)]
    [InlineData("missing@tld", true)]
    [InlineData("two@@at.ca", false)]
    [InlineData("spaces in@name.ca", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ReferralEmailValidationMatchesTheApiParse(string? email, bool expected)
    {
        Assert.Equal(expected, ReferralEmail.IsValidFormat(email));
    }

    [Fact]
    public void ReferralEmailRejectsAddressesLongerThanTheApiAccepts()
    {
        var tooLong = new string('a', 250) + "@example.ca";

        Assert.False(ReferralEmail.IsValidFormat(tooLong));
    }

    private static string ReadPageSource(string fileName)
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "src", "app", "EMA.App", "Components", "Pages", "Operations", fileName);

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

        throw new InvalidOperationException("Repository root was not found.");
    }
}
