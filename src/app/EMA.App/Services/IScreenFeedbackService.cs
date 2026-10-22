using EMA.App.Models;

namespace EMA.App.Services;

public interface IScreenFeedbackService
{
    Task<ScreenActionSummary> GetScreenActionSummaryAsync(
        int menuItemID,
        CancellationToken cancellationToken = default);

    Task<int> CreateFaqAsync(
        int menuItemID,
        CreateFaqRequest request,
        CancellationToken cancellationToken = default);

    Task<int> CreateGlobalFaqAsync(
        CreateFaqRequest request,
        CancellationToken cancellationToken = default);

    Task<int> CreateEnhancementRequestAsync(
        int menuItemID,
        CreateEnhancementRequest request,
        CancellationToken cancellationToken = default);

    Task<int> CreateSupportTicketAsync(
        int menuItemID,
        CreateSupportTicketRequest request,
        CancellationToken cancellationToken = default);

    Task SaveEnhancementVoteAsync(
        int enhancementRequestID,
        string vote,
        CancellationToken cancellationToken = default);

    Task<List<PublicFaq>> GetPublicFaqsAsync(
        CancellationToken cancellationToken = default);

    Task<List<FaqAdminItem>> GetFaqAdminAsync(
        CancellationToken cancellationToken = default);

    Task<string?> GetPublishedFaqHtmlAsync(
        CancellationToken cancellationToken = default);

    Task<PublicFaqPublishResponse> PublishPublicFaqPageAsync(
        CancellationToken cancellationToken = default);

    Task UpdateFaqAsync(
        int faqID,
        UpdateFaqAdminRequest request,
        CancellationToken cancellationToken = default);

    Task<EnhancementAdminResponse> GetEnhancementAdminAsync(
        CancellationToken cancellationToken = default);

    Task UpdateEnhancementAsync(
        int enhancementRequestID,
        UpdateEnhancementAdminRequest request,
        CancellationToken cancellationToken = default);

    Task<SupportTicketAdminResponse> GetSupportTicketAdminAsync(
        CancellationToken cancellationToken = default);

    Task UpdateSupportTicketAsync(
        int supportTicketID,
        UpdateSupportTicketAdminRequest request,
        CancellationToken cancellationToken = default);
}
