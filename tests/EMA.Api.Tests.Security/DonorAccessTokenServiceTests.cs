using System.Text;
using EMA.Api.Services.Authentication;
using Microsoft.Extensions.Configuration;

namespace EMA.Api.Tests.Security;

public sealed class DonorAccessTokenServiceTests
{
    [Fact]
    public void DonationChangesLinkIsBoundToDonationDonorAndOrganization()
    {
        var service =
            CreateService();

        var url =
            service.CreateDonationChangesUrl(
                donationID: 1234,
                donorID: 42,
                EMAOrganizationID: 9);

        var token =
            ExtractFragmentToken(
                url);

        var valid =
            service.TryValidateDonationChangesToken(
                token,
                donationID: 1234,
                out var identity);

        Assert.True(
            valid);

        Assert.NotNull(
            identity);

        Assert.Equal(
            42,
            identity!.DonorID);

        Assert.Equal(
            9,
            identity.EMAOrganizationID);

        Assert.StartsWith(
            "https://portal.example.test/donor-directed-changes/1234#token=",
            url);
    }

    [Fact]
    public void DonationChangesTokenCannotBeUsedForAnotherDonation()
    {
        var service =
            CreateService();

        var token =
            ExtractFragmentToken(
                service.CreateDonationChangesUrl(
                    donationID: 1234,
                    donorID: 42,
                    EMAOrganizationID: 9));

        var valid =
            service.TryValidateDonationChangesToken(
                token,
                donationID: 5678,
                out var identity);

        Assert.False(
            valid);

        Assert.Null(
            identity);
    }

    [Fact]
    public void TamperedDonationChangesTokenIsRejected()
    {
        var service =
            CreateService();

        var token =
            ExtractFragmentToken(
                service.CreateDonationChangesUrl(
                    donationID: 1234,
                    donorID: 42,
                    EMAOrganizationID: 9));

        var tokenParts =
            token.Split(
                '.',
                StringSplitOptions.None);

        Assert.Equal(
            2,
            tokenParts.Length);

        var signature =
            tokenParts[1];

        var firstSignatureCharacter =
            signature[0] == 'A'
                ? 'B'
                : 'A';

        var tamperedToken =
            $"{tokenParts[0]}." +
            firstSignatureCharacter +
            signature[1..];

        var valid =
            service.TryValidateDonationChangesToken(
                tamperedToken,
                donationID: 1234,
                out var identity);

        Assert.False(
            valid);

        Assert.Null(
            identity);
    }

    [Fact]
    public void DonorSessionTokenCannotBeUsedAsDonationChangesToken()
    {
        var service =
            CreateService();

        var token =
            service.CreateDonorSessionToken(
                donorID: 42,
                EMAOrganizationID: 9);

        var valid =
            service.TryValidateDonationChangesToken(
                token.Token,
                donationID: 1234,
                out var identity);

        Assert.False(
            valid);

        Assert.Null(
            identity);
    }

    private static DonorAccessTokenService
        CreateService()
    {
        var signingKey =
            Convert.ToBase64String(
                Encoding.UTF8.GetBytes(
                    "0123456789abcdef0123456789abcdef"));

        var configuration =
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["DONOR_ACCESS_TOKEN_SIGNING_KEY"] =
                            signingKey,

                        ["SqlConnectionString"] =
                            "Server=(local);Database=ema;Integrated Security=true;",

                        ["DONOR_PORTAL_BASE_URL"] =
                            "https://portal.example.test"
                    })
                .Build();

        return new DonorAccessTokenService(
            configuration);
    }

    private static string ExtractFragmentToken(
        string url)
    {
        var fragment =
            new Uri(
                url)
                .Fragment;

        const string prefix =
            "#token=";

        Assert.StartsWith(
            prefix,
            fragment);

        return Uri.UnescapeDataString(
            fragment[
                prefix.Length..]);
    }
}
