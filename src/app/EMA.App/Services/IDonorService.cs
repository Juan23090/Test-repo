using EMA.App.Models;
using System.Text.Json;

namespace EMA.App.Services;

public interface IDonorService
{
    Task<List<DonorItem>> GetAllAsync(int orgID);
    Task<DonorItem?> GetByIDAsync(int donorID);
    Task<DonorItem?> GetOlderAsync(int orgID, int currentID);
    Task<DonorItem?> GetNewerAsync(int orgID, int currentID);
    Task<int?> CreateAsync(DonorItem donor);
    Task UpdateAsync(DonorItem donor);
    Task<List<DonorItem>> SearchAsync(
        int orgID,
        string query,
        string? field = null,
        bool exact = false,
        CancellationToken cancellationToken = default);
    Task<List<JsonElement>> GetDonorAuditAsync(
        int orgID,
        int donorID,
        CancellationToken cancellationToken = default);
    Task<bool> UpdateJourneyDetailsAsync(int orgID, DonorItem donor);
    Task<List<DonorJourneyActivityType>> GetJourneyActivityTypesAsync(int orgID);
    Task<List<UserItem>> GetOrganizationUsersAsync(int orgID);
    Task<List<DonorJourneyEntry>> GetJourneyHistoryAsync(int orgID, int donorID);
    Task<string?> CreateJourneyEntryAsync(int orgID, int donorID, DonorJourneyEntry entry);
}
