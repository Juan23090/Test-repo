using System.Linq;
using System.Reflection;
using EMA.Api.Functions.Donations;
using EMA.Api.Services.Security;
using Microsoft.Azure.Functions.Worker;

namespace EMA.Api.Tests.Security;

/*
 * CreateDonation and UpdateDonation were re-enabled after being disabled
 * by the security patch. These tests pin the guarantees that justified
 * re-enabling them.
 */
public sealed class DonationWriteAccessPolicyTests
{
    private const int AuthenticatedOrganizationID = 20;
    private const int AuthenticatedUserID = 13;
    private const int OtherOrganizationID = 21;

    [Fact]
    public void RouteOrganizationMatchingTheTokenIsAllowed()
    {
        var identity =
            CreateOrganizationUserIdentity(
                AuthenticatedOrganizationID);

        Assert.True(
            DonationWriteAccessPolicy
                .IsRouteOrganizationAllowed(
                    identity,
                    AuthenticatedOrganizationID));
    }

    [Theory]
    [InlineData(OtherOrganizationID)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void RouteOrganizationDifferingFromTheTokenIsDenied(
        int routeOrganizationID)
    {
        var identity =
            CreateOrganizationUserIdentity(
                AuthenticatedOrganizationID);

        Assert.False(
            DonationWriteAccessPolicy
                .IsRouteOrganizationAllowed(
                    identity,
                    routeOrganizationID));
    }

    [Fact]
    public void OrganizationAlwaysComesFromTheTokenNotTheRoute()
    {
        var identity =
            CreateOrganizationUserIdentity(
                AuthenticatedOrganizationID);

        Assert.Equal(
            AuthenticatedOrganizationID,
            DonationWriteAccessPolicy
                .ResolveOrganizationID(
                    identity));
    }

    [Fact]
    public void ActingUserAlwaysComesFromTheTokenNotTheRequestBody()
    {
        var identity =
            CreateOrganizationUserIdentity(
                AuthenticatedOrganizationID);

        /*
         * A client sending CreatedbyUserID / UpdatedbyUserID cannot
         * influence this. The previous implementation fell back to
         * "donation.CreatedbyUserID ?? donation.UpdatedbyUserID ?? 1",
         * which both trusted the client and silently attributed
         * unattributed writes to user 1.
         */
        Assert.Equal(
            AuthenticatedUserID,
            DonationWriteAccessPolicy
                .ResolveActingUserID(
                    identity));
    }

    [Fact]
    public void PolicyRejectsAMissingIdentityRatherThanDefaulting()
    {
        Assert.Throws<ArgumentNullException>(
            () => DonationWriteAccessPolicy
                .ResolveOrganizationID(
                    null!));

        Assert.Throws<ArgumentNullException>(
            () => DonationWriteAccessPolicy
                .ResolveActingUserID(
                    null!));
    }

    [Theory]
    [InlineData(typeof(CreateDonation))]
    [InlineData(typeof(UpdateDonation))]
    public void DonationWriteEndpointsUseTheCentralizedTransportPolicy(
        Type functionType)
    {
        /*
         * AuthorizationLevel.Function would bypass the security
         * middleware with a host key, skipping authentication, session
         * validation, rate limiting and tenant authorization.
         */
        var trigger =
            functionType
                .GetMethod("Run")!
                .GetParameters()
                .Select(parameter =>
                    parameter.GetCustomAttribute<HttpTriggerAttribute>())
                .Single(attribute => attribute is not null)!;

        Assert.Equal(
            AuthorizationLevel.Anonymous,
            trigger.AuthLevel);
    }

    private static ApiRequestIdentity
        CreateOrganizationUserIdentity(
            int organizationID)
    {
        var issued =
            DateTimeOffset.UtcNow;

        return new ApiRequestIdentity(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ApiActorType.OrganizationUser,
            organizationID,
            UserID: AuthenticatedUserID,
            DonorID: null,
            RoleIDs: Array.Empty<int>(),
            AllowedCategoryIDs: new[] { 10, 20 },
            IssuedAtUtc: issued,
            ExpiresAtUtc: issued.AddHours(1));
    }
}
