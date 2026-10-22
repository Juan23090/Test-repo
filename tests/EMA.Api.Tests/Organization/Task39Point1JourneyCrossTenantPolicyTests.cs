using EMA.Api.Services.Security;

namespace EMA.Api.Tests.Organization;

public sealed class Task39Point1JourneyCrossTenantPolicyTests
{
    [Fact]
    public void JourneyFeedbackKeepsOrganizationUserAccessAndEnablesOnlyItsPlatformCrossTenantBoundary()
    {
        var registry = new ApiEndpointPolicyRegistry();

        var journeyFeedback = registry.GetPolicy("GetOrganizationJourneyFeedback");
        Assert.Equal(ApiEndpointAccess.OrganizationUser, journeyFeedback.Access);
        Assert.True(journeyFeedback.AllowPlatformCrossTenant);

        var ordinaryOrganizationEndpoint = registry.GetPolicy("GetOrganizationJourneyFeedback_NotReal");
        Assert.Equal(ApiEndpointAccess.OrganizationUser, ordinaryOrganizationEndpoint.Access);
        Assert.False(ordinaryOrganizationEndpoint.AllowPlatformCrossTenant);
    }
}
