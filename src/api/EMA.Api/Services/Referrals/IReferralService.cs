using EMA.Api.Models;

namespace EMA.Api.Services.Referrals;

public interface IReferralService
{
    Task<ReferralEligibilityDto> GetEligibilityAsync(
        int orgID,
        CancellationToken cancellationToken = default);

    Task<SendReferralsResult> SendBatchReferralsAsync(
        int orgID,
        int userID,
        SendReferralsRequest request,
        CancellationToken cancellationToken = default);

    Task<AcceptReferralResult> AcceptReferralAsync(
        int referredOrgID,
        int userID,
        string referralCode,
        string referralEmail,
        CancellationToken cancellationToken = default);

    Task<ReferralSummaryDto> GetReferralHistoryAsync(
        int orgID,
        CancellationToken cancellationToken = default);
}
