using EMA.Api.DTOs;

namespace EMA.Api.Services.ScreenFeedback;

public interface IScreenFeedbackRepository
{
    Task<bool> MenuItemExistsAsync(
        int menuItemID,
        CancellationToken cancellationToken = default);

    Task<ScreenActionSummaryDto> GetScreenActionSummaryAsync(
        int menuItemID,
        int organizationID,
        CancellationToken cancellationToken = default);

    Task<OrganizationJourneyFeedbackSummaryDto> GetOrganizationJourneyFeedbackAsync(
        int organizationID,
        CancellationToken cancellationToken = default);

    Task<int> CreateFaqAsync(
        int menuItemID,
        int organizationID,
        int userID,
        string question,
        string answer,
        CancellationToken cancellationToken = default);


    Task<int> CreateGlobalFaqAsync(
        int organizationID,
        int userID,
        string question,
        string answer,
        CancellationToken cancellationToken = default);

    Task<int> CreateEnhancementRequestAsync(
        int menuItemID,
        int organizationID,
        int userID,
        string title,
        string description,
        CancellationToken cancellationToken = default);

    Task<int> CreateSupportTicketAsync(
        int menuItemID,
        int organizationID,
        int userID,
        string title,
        string description,
        CancellationToken cancellationToken = default);

    Task<bool> SaveEnhancementVoteAsync(
        int enhancementRequestID,
        int organizationID,
        int userID,
        string vote,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PublicFaqDto>> GetPublicFaqsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FaqAdminDto>> GetFaqAdminAsync(
        CancellationToken cancellationToken = default);

    Task<ScreenFeedbackSaveResult> UpdateFaqAsync(
        int faqID,
        int userID,
        UpdateFaqAdminRequestDto request,
        CancellationToken cancellationToken = default);

    Task<EnhancementAdminResponseDto> GetEnhancementAdminAsync(
        CancellationToken cancellationToken = default);

    Task<ScreenFeedbackSaveResult> UpdateEnhancementAsync(
        int enhancementRequestID,
        int userID,
        UpdateEnhancementAdminRequestDto request,
        CancellationToken cancellationToken = default);

    Task<SupportTicketAdminResponseDto> GetSupportTicketAdminAsync(
        CancellationToken cancellationToken = default);

    Task<ScreenFeedbackSaveResult> UpdateSupportTicketAsync(
        int supportTicketID,
        int userID,
        UpdateSupportTicketAdminRequestDto request,
        CancellationToken cancellationToken = default);

    Task<int> ReviewExpiredEnhancementRequestsAsync(
        int automatedUserID,
        CancellationToken cancellationToken = default);
}

public enum ScreenFeedbackSaveResult
{
    Success = 0,
    NotFound = 1,
    InvalidStatus = 2
}
