using System.Security.Cryptography;
using EMA.Api.Services.Security;
using Microsoft.Extensions.Configuration;

namespace EMA.Api.Tests.Security;

public sealed class ApiAccessTokenServiceTests
{
    [Fact]
    public void RoundTripPreservesAuthoritativeIdentity()
    {
        var service =
            CreateService();

        var expected =
            CreateIdentity();

        var token =
            service.CreateToken(
                expected);

        Assert.True(
            service.TryValidateToken(
                token.Token,
                out var actual));

        Assert.NotNull(
            actual);

        Assert.Equal(
            expected.SessionID,
            actual.SessionID);

        Assert.Equal(
            expected.SecurityStamp,
            actual.SecurityStamp);

        Assert.Equal(
            expected.EMAOrganizationID,
            actual.EMAOrganizationID);

        Assert.Equal(
            expected.UserID,
            actual.UserID);

        Assert.Equal(
            expected.RoleIDs,
            actual.RoleIDs);

        Assert.Equal(
            expected.AllowedCategoryIDs,
            actual.AllowedCategoryIDs);
    }

    [Fact]
    public void TamperedPayloadIsRejected()
    {
        var service =
            CreateService();

        var token =
            service.CreateToken(
                CreateIdentity())
                .Token;

        var parts =
            token.Split('.');

        parts[1] =
            (parts[1][0] == 'A'
                ? "B"
                : "A") +
            parts[1][1..];

        Assert.False(
            service.TryValidateToken(
                string.Join(
                    '.',
                    parts),
                out _));
    }

    [Fact]
    public void WrongAudienceIsRejected()
    {
        var issuer =
            CreateService(
                audience:
                    "ema-browser");

        var validator =
            CreateService(
                audience:
                    "different-client");

        var token =
            issuer.CreateToken(
                CreateIdentity())
                .Token;

        Assert.False(
            validator.TryValidateToken(
                token,
                out _));
    }

    [Fact]
    public void ExpiredTokenIsRejected()
    {
        var service =
            CreateService();

        var now =
            DateTimeOffset.UtcNow;

        var token =
            service.CreateToken(
                CreateIdentity() with
                {
                    IssuedAtUtc =
                        now.AddMinutes(-10),

                    ExpiresAtUtc =
                        now.AddMinutes(-1)
                })
                .Token;

        Assert.False(
            service.TryValidateToken(
                token,
                out _));
    }

    [Fact]
    public void PrincipalRoundTripRejectsDonorShapeForStaff()
    {
        var invalidIdentity =
            CreateIdentity() with
            {
                DonorID = 42
            };

        Assert.False(
            ApiRequestIdentity.TryFromPrincipal(
                invalidIdentity
                    .ToClaimsPrincipal(),
                out _));
    }

    private static ApiAccessTokenService
        CreateService(
            string audience =
                "ema-browser")
    {
        var key =
            Convert.ToBase64String(
                SHA256.HashData(
                    "unit-test-signing-key"u8
                        .ToArray()));

        var configuration =
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<
                        string,
                        string?>
                    {
                        ["API_ACCESS_TOKEN_SIGNING_KEY"] =
                            key,

                        ["API_ACCESS_TOKEN_ISSUER"] =
                            "https://unit.test",

                        ["API_ACCESS_TOKEN_AUDIENCE"] =
                            audience,

                        ["API_ACCESS_TOKEN_MAX_LIFETIME_MINUTES"] =
                            "120"
                    })
                .Build();

        return new ApiAccessTokenService(
            configuration);
    }

    private static ApiRequestIdentity
        CreateIdentity()
    {
        var issuedAtUtc =
            DateTimeOffset.UtcNow;

        return new ApiRequestIdentity(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ApiActorType.OrganizationUser,
            EMAOrganizationID: 27,
            UserID: 101,
            DonorID: null,
            RoleIDs:
                new[]
                {
                    4,
                    9
                },
            AllowedCategoryIDs:
                new[]
                {
                    10,
                    20
                },
            IssuedAtUtc:
                issuedAtUtc,
            ExpiresAtUtc:
                issuedAtUtc.AddHours(1));
    }
}

