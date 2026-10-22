using EMA.App.Models;

namespace EMA.App.Services
{
    /// <summary>
    /// Defines CRUD operations for the Customization lookup tables.
    /// </summary>
    public interface ICustomizationService
    {
        // Events (formerly Currency Types)
        Task<List<EventItem>> GetEventsAsync(int EMAOrganizationID, bool activeOnly = false);
        Task UpdateEventAsync(EventItem item);
        Task SetEventActiveAsync(int id, bool isActive);

        // Donation Sources (no hard delete — deactivate via IsActive)
        Task<List<DonationSourceItem>> GetDonationSourcesAsync(int EMAOrganizationID, bool activeOnly = false);
        Task UpdateDonationSourceAsync(DonationSourceItem item);
        Task SetDonationSourceActiveAsync(int id, bool isActive);
        Task<int> InsertDonationSourceAsync(int EMAOrganizationID, string description);

        // Tribute Types (no hard delete — deactivate via IsActive)
        Task<List<TributeTypeItem>> GetTributeTypesAsync(int EMAOrganizationID, bool activeOnly = false);
        Task UpdateTributeTypeAsync(TributeTypeItem item);
        Task SetTributeTypeActiveAsync(int id, bool isActive);
        Task<int> InsertTributeTypeAsync(int EMAOrganizationID, string description);

        // Methods of Giving
        Task<List<MethodOfGivingItem>> GetMethodsOfGivingAsync(int EMAOrganizationID, bool activeOnly = false);
        Task UpdateMethodOfGivingAsync(MethodOfGivingItem item);
        Task SetMethodOfGivingActiveAsync(int id, bool isActive);
        Task SetMethodOfGivingDefaultAsync(int EMAOrganizationID, int id);

        // Campuses
        Task<List<CampusItem>> GetCampusesAsync(int EMAOrganizationID, bool activeOnly = false);
        Task UpdateCampusAsync(CampusItem item);
        Task SetCampusActiveAsync(int id, bool isActive);
        Task SetCampusDefaultAsync(int EMAOrganizationID, int id);

        // Funds
        Task<List<FundItem>> GetFundsAsync(int EMAOrganizationID, bool activeOnly = false);
        Task UpdateFundAsync(FundItem item);
        Task SetFundActiveAsync(int id, bool isActive);
        Task SetFundDefaultAsync(int EMAOrganizationID, int id);
        Task DeleteMethodOfGivingAsync(int EMAOrganizationID, int id);
        Task DeleteCampusAsync(int EMAOrganizationID, int id);
        Task DeleteFundAsync(int EMAOrganizationID, int id);

        // Per-user fund shortlist (drives which funds appear in the Donations fund dropdown).
        // The signed-in user is resolved server-side from the request identity.
        Task<List<UserFundShortListItem>> GetUserFundShortListAsync(int EMAOrganizationID);
        Task<bool> SaveUserFundShortListAsync(int EMAOrganizationID, List<UserFundShortListItem> items);

        Task<int> InsertEventAsync(int EMAOrganizationID, string description);
        Task<int> InsertMethodOfGivingAsync(int EMAOrganizationID, string description);
        Task<int> InsertCampusAsync(int EMAOrganizationID, string campusName);
        Task<int> InsertFundAsync(int EMAOrganizationID, string fundName);

        // Copy EMA (Org 1) active default entries into the current org for one entity.
        // Ok is false when the request failed (so the UI can report it); Copied is the row count;
        // Message is an optional server note (e.g. self-copy / empty-source).
        Task<(bool Ok, int Copied, string? Message)> CopyDefaultEntitiesAsync(int EMAOrganizationID, string entitySlug);

        // Org Settings
        Task<System.Text.Json.JsonElement?> GetOrgSettingsAsync(int EMAOrganizationID);
        Task UpdateOrgSettingsAsync(int EMAOrganizationID, object payload);
        Task<bool> SetLandingGraphAsync(int EMAOrganizationID, string graph, string target);

        // Anonymous donor (the donor used for anonymous donations)
        Task UpdateOrgAnonymousDonorAsync(int EMAOrganizationID, int donorID);

        // Entity Aliases (key-value override, no add/remove, no IsActive)
        Task<List<EntityAliasItem>> GetEntityAliasesAsync(int EMAOrganizationID);
        Task UpsertEntityAliasAsync(int EMAOrganizationID, EntityAliasItem item);

        // Interaction Types (no hard delete — deactivate via IsActive)
        Task<List<InteractionTypeItem>> GetInteractionTypesAsync(int EMAOrganizationID, bool activeOnly = false);
        Task UpdateInteractionTypeAsync(InteractionTypeItem item);
        Task SetInteractionTypeActiveAsync(int id, bool isActive);
        Task<int> InsertInteractionTypeAsync(int EMAOrganizationID, string description);

        // Donor Status (IsActive is a real bit; no hard delete). Toggle uses the full-row
        // UpdateDonorStatusAsync so the NOT NULL Description is always sent.
        Task<List<DonorStatusItem>> GetDonorStatusesAsync(int EMAOrganizationID, bool activeOnly = false);
        Task UpdateDonorStatusAsync(DonorStatusItem item);
        Task<int> InsertDonorStatusAsync(int EMAOrganizationID, string description);

        // PAD Defaults & Customization Data
        Task<PADCustomizationData?> GetCustomizationDataAsync(int EMAOrganizationID);
        Task SavePADDefaultsAsync(int EMAOrganizationID, int? eventID, int? sourceID, int? methodOfGivingID, int? fundID, int? campusID);
    }
}
