using EMA.Api.Functions.Organization;
using EMA.Api.Services.Security;
using Microsoft.Azure.Functions.Worker;
using System.Reflection;

namespace EMA.Api.Tests.Organization;

public sealed class Task39Point1JourneyFeedbackContractTests
{
    [Fact]
    public void OrganizationJourneyFeedbackUsesExpectedRoute()
    {
        var method = typeof(GetOrganizationJourneyFeedback).GetMethod(nameof(GetOrganizationJourneyFeedback.Run));
        Assert.NotNull(method);
        Assert.Equal("GetOrganizationJourneyFeedback", method!.GetCustomAttribute<FunctionAttribute>()?.Name);
    }

    [Fact]
    public void OrganizationJourneyFeedbackUsesOrganizationUserBoundaryBeforeGranularRouteCheck()
    {
        var registry = new ApiEndpointPolicyRegistry();
        var policy = registry.GetPolicy("GetOrganizationJourneyFeedback");
        Assert.Equal(ApiEndpointAccess.OrganizationUser, policy.Access);
    }
}
