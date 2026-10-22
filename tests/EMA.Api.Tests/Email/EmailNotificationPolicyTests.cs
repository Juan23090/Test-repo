using EMA.Api.Services.Email;

namespace EMA.Api.Tests.Email;

public sealed class EmailNotificationPolicyTests
{
    [Fact]
    public void GivingMethodMustAllowDonorAcknowledgements()
    {
        var reason =
            EmailNotificationService
                .GetAcknowledgementSkipReason(
                    methodAcknowledgesDonor: false,
                    manualAcknowledgementsEnabled: true,
                    automatedAcknowledgementsEnabled: true,
                    transactionType: DonationTransactionType.Automated);

        Assert.Equal(
            "The donation's giving method is not configured to acknowledge the donor.",
            reason);
    }

    [Theory]
    [InlineData(
        DonationTransactionType.Manual,
        false,
        true,
        "The donor has disabled manual transaction acknowledgements.")]
    [InlineData(
        DonationTransactionType.Automated,
        true,
        false,
        "The donor has disabled automated transaction acknowledgements.")]
    public void MatchingDonorPreferenceMustBeEnabled(
        DonationTransactionType transactionType,
        bool manualEnabled,
        bool automatedEnabled,
        string expectedReason)
    {
        var reason =
            EmailNotificationService
                .GetAcknowledgementSkipReason(
                    methodAcknowledgesDonor: true,
                    manualAcknowledgementsEnabled: manualEnabled,
                    automatedAcknowledgementsEnabled: automatedEnabled,
                    transactionType: transactionType);

        Assert.Equal(
            expectedReason,
            reason);
    }

    [Theory]
    [InlineData(
        DonationTransactionType.Manual,
        true,
        false)]
    [InlineData(
        DonationTransactionType.Automated,
        false,
        true)]
    public void MatchingDonorPreferenceAllowsAcknowledgement(
        DonationTransactionType transactionType,
        bool manualEnabled,
        bool automatedEnabled)
    {
        var reason =
            EmailNotificationService
                .GetAcknowledgementSkipReason(
                    methodAcknowledgesDonor: true,
                    manualAcknowledgementsEnabled: manualEnabled,
                    automatedAcknowledgementsEnabled: automatedEnabled,
                    transactionType: transactionType);

        Assert.Null(
            reason);
    }
}
