using EMA.Api.Services.Security;

namespace EMA.Api.Tests.MailingLists;

public sealed class MailingListsSecurityPolicyTests
{
    [Theory]
    [InlineData("GetMailingLists")]
    [InlineData("GetMailingListDetails")]
    [InlineData("CreateMailingList")]
    [InlineData("UpdateMailingList")]
    [InlineData("PrepareMailingList")]
    [InlineData("ExportMailingListCSV")]
    [InlineData("GetReviewDonors")]
    [InlineData("ToggleDonorOptStatus")]
    public void AllMailingListEndpointsRequireOrganizationAuthentication(string functionName)
    {
        var policy = new ApiEndpointPolicyRegistry().GetPolicy(functionName);
        Assert.Equal(ApiEndpointAccess.OrganizationUser, policy.Access);
    }
}
