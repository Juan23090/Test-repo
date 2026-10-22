using EMA.App.Models;

namespace EMA.App.Services;

public interface IReferralClientService
{
    Task<ReferralEligibilityDto?> GetEligibilityAsync(
        int orgID,
        CancellationToken cancellationToken = default);

    Task<SendReferralsResult?> SendBatchReferralsAsync(
        int orgID,
        SendReferralsRequest request,
        CancellationToken cancellationToken = default);

    Task<AcceptReferralResult?> AcceptReferralAsync(
        int orgID,
        AcceptReferralRequest request,
        CancellationToken cancellationToken = default);

    Task<ReferralSummaryDto?> GetReferralHistoryAsync(
        int orgID,
        CancellationToken cancellationToken = default);
}
