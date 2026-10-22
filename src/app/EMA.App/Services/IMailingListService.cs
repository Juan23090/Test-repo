using EMA.App.Models;

namespace EMA.App.Services;

public interface IMailingListService
{
    Task<List<MailingListItem>> GetMailingListsAsync(int orgID, bool showInactive = false);
    Task<MailingListDetailsModel?> GetMailingListDetailsAsync(int orgID, int listID);
    Task<int> PrepareMailingListAsync(int orgID, int listID);
    Task<byte[]> ExportMailingListInstanceAsync(int orgID, int instanceID);
    Task<int> CreateMailingListAsync(int orgID, CreateMailingListModel model);
    Task UpdateMailingListAsync(int orgID, int listID, CreateMailingListModel model);
    Task<List<ReviewDonorItem>> GetReviewDonorsAsync(int orgID, int listID, string? search = null);
    Task ToggleDonorOptStatusAsync(int orgID, int listID, int donorID, bool? optedIn, bool? optedOut);
}
