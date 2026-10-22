using EMA.App.Models;
using System.Net.Http.Json;

namespace EMA.App.Services;

/// <summary>
/// Real implementation of ICustomizationService calling Azure Functions API.
/// Write operations pending stored procedures from Roger.
/// </summary>
public class CustomizationService : ICustomizationService
{
    private readonly HttpClient _http;
    private const string ApiBase = "/api";

    public CustomizationService(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<EventItem>> GetEventsAsync(int orgID, bool activeOnly = false)
    {
        try
        {
            var url = activeOnly ? $"{ApiBase}/customization/events/{orgID}?activeOnly=true" : $"{ApiBase}/customization/events/{orgID}";
            var result = await _http.GetFromJsonAsync<List<EventItem>>(url);
            return result ?? new();
        }
        catch { return new(); }
    }

    public async Task<List<MethodOfGivingItem>> GetMethodsOfGivingAsync(int orgID, bool activeOnly = false)
    {
        try
        {
            var url = activeOnly ? $"{ApiBase}/customization/methods/{orgID}?activeOnly=true" : $"{ApiBase}/customization/methods/{orgID}";
            var result = await _http.GetFromJsonAsync<List<MethodOfGivingItem>>(url);
            return result ?? new();
        }
        catch { return new(); }
    }

    public async Task<List<CampusItem>> GetCampusesAsync(int orgID, bool activeOnly = false)
    {
        try
        {
            var url = activeOnly ? $"{ApiBase}/customization/campuses/{orgID}?activeOnly=true" : $"{ApiBase}/customization/campuses/{orgID}";
            var result = await _http.GetFromJsonAsync<List<CampusItem>>(url);
            return result ?? new();
        }
        catch { return new(); }
    }

    public async Task<List<FundItem>> GetFundsAsync(int orgID, bool activeOnly = false)
    {
        try
        {
            var url = activeOnly ? $"{ApiBase}/customization/funds/{orgID}?activeOnly=true" : $"{ApiBase}/customization/funds/{orgID}";
            var result = await _http.GetFromJsonAsync<List<FundItem>>(url);
            return result ?? new();
        }
        catch { return new(); }
    }

    public async Task UpdateEventAsync(EventItem item)
    {
        try
        {
            var payload = new { Description = item.Description, IsActive = item.IsActive ? "Y" : "N", Sequence = item.Sequence };
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            await _http.PutAsync($"{ApiBase}/customization/events/{item.EMAEventID}", content);
        }
        catch { }
    }
    public async Task UpdateMethodOfGivingAsync(MethodOfGivingItem item)
    {
        try
        {
            var payload = new
            {
                Description = item.Description,
                IsActive = item.IsActive ? "Y" : "N",
                CRAReceiptable = item.CRAReceiptable ? "Y" : "N",
                JournalEntryInDetail = item.JournalEntryInDetail == true ? "Y" : "N",
                AcknowledgeToDonor = item.AcknowledgeToDonor == true ? "Y" : "N",
                AcknowledgeToOrganization = item.AcknowledgeToOrganization == true ? "Y" : "N",
                AcknowledgeToEMASupport = item.AcknowledgeToEMASupport == true ? "Y" : "N",
                AcknowledgementMessage = item.AcknowledgementMessage,
                Sequence = item.Sequence
            };
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            await _http.PutAsync($"{ApiBase}/customization/methods/{item.EMAMethodOfGivingID}", content);
        }
        catch { }
    }
    public async Task UpdateCampusAsync(CampusItem item)
    {
        try
        {
            var payload = new { Name = item.CampusName, IsActive = item.CampusActive ? "Y" : "N", Sequence = item.Sequence, DefaultCampusFundID = item.DefaultCampusFundID };
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            await _http.PutAsync($"{ApiBase}/customization/campuses/{item.EMACampusID}", content);
        }
        catch { }
    }
    // ── Per-user fund shortlist ───────────────────────────────────────────────
    // The API reconciles the list against the org's current funds before returning it, so the
    // caller always gets every active fund with the user's existing checkmarks preserved.

    public async Task<List<UserFundShortListItem>> GetUserFundShortListAsync(int orgID)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<List<UserFundShortListItem>>(
                $"{ApiBase}/customization/user-fund-shortlist/{orgID}");

            return result ?? new List<UserFundShortListItem>();
        }
        catch
        {
            return new List<UserFundShortListItem>();
        }
    }

    // Returns false when the save failed, so the overlay can tell the user instead of silently
    // discarding their edits.
    public async Task<bool> SaveUserFundShortListAsync(int orgID, List<UserFundShortListItem> items)
    {
        try
        {
            var response = await _http.PutAsJsonAsync(
                $"{ApiBase}/customization/user-fund-shortlist/{orgID}",
                new { Items = items });

            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task UpdateFundAsync(FundItem item)
    {
        try
        {
            var payload = new { Name = item.FundName, IsActive = item.FundActive ? "Y" : "N", TaxDeductible = item.TaxDeductible ? "Y" : "N", EMACampusID = item.EMACampusID, Sequence = item.Sequence };
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            await _http.PutAsync($"{ApiBase}/customization/funds/{item.EMAFundID}", content);
        }
        catch { }
    }
    public async Task SetEventActiveAsync(int id, bool isActive)
    {
        try
        {
            var payload = new { IsActive = isActive ? "Y" : "N" };
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            await _http.PutAsync($"{ApiBase}/customization/events/{id}", content);
        }
        catch { }
    }
    public async Task SetMethodOfGivingActiveAsync(int id, bool isActive)
    {
        try
        {
            var payload = new { IsActive = isActive ? "Y" : "N" };
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            await _http.PutAsync($"{ApiBase}/customization/methods/{id}", content);
        }
        catch { }
    }
    public async Task SetCampusActiveAsync(int id, bool isActive)
    {
        try
        {
            var payload = new { IsActive = isActive ? "Y" : "N" };
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            await _http.PutAsync($"{ApiBase}/customization/campuses/{id}", content);
        }
        catch { }
    }
    public async Task SetFundActiveAsync(int id, bool isActive)
    {
        try
        {
            var payload = new { IsActive = isActive ? "Y" : "N" };
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            await _http.PutAsync($"{ApiBase}/customization/funds/{id}", content);
        }
        catch { }
    }
    public async Task DeleteMethodOfGivingAsync(int orgID, int id)
    {
        try { await _http.DeleteAsync($"{ApiBase}/customization/methods/{orgID}/{id}"); }
        catch { }
    }

    public async Task DeleteCampusAsync(int orgID, int id)
    {
        try { await _http.DeleteAsync($"{ApiBase}/customization/campuses/{orgID}/{id}"); }
        catch { }
    }

    public async Task DeleteFundAsync(int orgID, int id)
    {
        try { await _http.DeleteAsync($"{ApiBase}/customization/funds/{orgID}/{id}"); }
        catch { }
    }

    public async Task<int> InsertEventAsync(int orgID, string description)
    {
        try
        {
            var payload = new { EMAOrganizationID = orgID, Description = description };
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{ApiBase}/customization/events", content);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                return result.GetProperty("EMAEventID").GetInt32();
            }
        }
        catch { }
        return 0;
    }

    public async Task<int> InsertMethodOfGivingAsync(int orgID, string description)
    {
        try
        {
            var payload = new { EMAOrganizationID = orgID, Description = description };
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{ApiBase}/customization/methods", content);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                return result.GetProperty("EMAMethodOfGivingID").GetInt32();
            }
        }
        catch { }
        return 0;
    }

    public async Task<int> InsertCampusAsync(int orgID, string campusName)
    {
        try
        {
            var payload = new { EMAOrganizationID = orgID, CampusName = campusName };
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{ApiBase}/customization/campuses", content);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                return result.TryGetProperty("EMACampusID", out var cid) ? cid.GetInt32() : 0;
            }
        }
        catch { }
        return 0;
    }

    public async Task<int> InsertFundAsync(int orgID, string fundName)
    {
        try
        {
            var payload = new { EMAOrganizationID = orgID, FundName = fundName };
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{ApiBase}/customization/funds", content);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                return result.GetProperty("EMAFundID").GetInt32();
            }
        }
        catch { }
        return 0;
    }

    public Task SetMethodOfGivingDefaultAsync(int orgID, int id) => Task.CompletedTask;

    public Task SetCampusDefaultAsync(int orgID, int id) => Task.CompletedTask;

    public Task SetFundDefaultAsync(int orgID, int id) => Task.CompletedTask;

    // ---- Donation Sources ----
    public async Task<List<DonationSourceItem>> GetDonationSourcesAsync(int orgID, bool activeOnly = false)
    {
        try
        {
            var url = activeOnly ? $"{ApiBase}/customization/donation-sources/{orgID}?activeOnly=true" : $"{ApiBase}/customization/donation-sources/{orgID}";
            var result = await _http.GetFromJsonAsync<List<DonationSourceItem>>(url);
            return result ?? new();
        }
        catch { return new(); }
    }

    public async Task UpdateDonationSourceAsync(DonationSourceItem item)
    {
        try
        {
            var payload = new { Description = item.Description, IsActive = item.IsActive ? "Y" : "N", Sequence = item.Sequence };
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            await _http.PutAsync($"{ApiBase}/customization/donation-sources/{item.EMADonationSourceID}", content);
        }
        catch { }
    }

    public async Task SetDonationSourceActiveAsync(int id, bool isActive)
    {
        try
        {
            var payload = new { IsActive = isActive ? "Y" : "N" };
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            await _http.PutAsync($"{ApiBase}/customization/donation-sources/{id}", content);
        }
        catch { }
    }

    public async Task<int> InsertDonationSourceAsync(int orgID, string description)
    {
        try
        {
            var payload = new { EMAOrganizationID = orgID, Description = description };
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{ApiBase}/customization/donation-sources", content);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                return result.GetProperty("EMADonationSourceID").GetInt32();
            }
        }
        catch { }
        return 0;
    }

    // ---- Tribute Types ----
    public async Task<List<TributeTypeItem>> GetTributeTypesAsync(int orgID, bool activeOnly = false)
    {
        try
        {
            var url = activeOnly ? $"{ApiBase}/customization/tribute-types/{orgID}?activeOnly=true" : $"{ApiBase}/customization/tribute-types/{orgID}";
            var result = await _http.GetFromJsonAsync<List<TributeTypeItem>>(url);
            return result ?? new();
        }
        catch { return new(); }
    }

    public async Task UpdateTributeTypeAsync(TributeTypeItem item)
    {
        try
        {
            var payload = new { Description = item.Description, IsActive = item.IsActive ? "Y" : "N", Sequence = item.Sequence };
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            await _http.PutAsync($"{ApiBase}/customization/tribute-types/{item.EMATributeTypeID}", content);
        }
        catch { }
    }

    public async Task SetTributeTypeActiveAsync(int id, bool isActive)
    {
        try
        {
            var payload = new { IsActive = isActive ? "Y" : "N" };
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            await _http.PutAsync($"{ApiBase}/customization/tribute-types/{id}", content);
        }
        catch { }
    }

    public async Task<int> InsertTributeTypeAsync(int orgID, string description)
    {
        try
        {
            var payload = new { EMAOrganizationID = orgID, Description = description };
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{ApiBase}/customization/tribute-types", content);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                return result.GetProperty("EMATributeTypeID").GetInt32();
            }
        }
        catch { }
        return 0;
    }
    public async Task<(bool Ok, int Copied, string? Message)> CopyDefaultEntitiesAsync(int orgID, string entitySlug)
    {
        try
        {
            var response = await _http.PostAsync($"{ApiBase}/customization/{entitySlug}/copy-defaults/{orgID}", null);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                var copied = result.TryGetProperty("copied", out var c) && c.TryGetInt32(out var n) ? n : 0;
                var message = result.TryGetProperty("message", out var m) ? m.GetString() : null;
                return (true, copied, message);
            }
            return (false, 0, null);
        }
        catch { return (false, 0, null); }
    }

    public async Task<System.Text.Json.JsonElement?> GetOrgSettingsAsync(int EMAOrganizationID)
    {
        try
        {
            var response = await _http.GetAsync($"{ApiBase}/customization/settings/{EMAOrganizationID}");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
            }
            return null;
        }
        catch { return null; }
    }
    public async Task UpdateOrgSettingsAsync(int EMAOrganizationID, object payload)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            await _http.PostAsync($"{ApiBase}/customization/settings/{EMAOrganizationID}", content);
        }
        catch { }
    }

    public async Task<bool> SetLandingGraphAsync(int EMAOrganizationID, string graph, string target)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(new { graph, target });
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{ApiBase}/customization/landing-graph/{EMAOrganizationID}", content);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task UpdateOrgAnonymousDonorAsync(int EMAOrganizationID, int donorID)
    {
        try
        {
            var payload = new { EMADonorIDForAnonymous = donorID };
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            await _http.PutAsync($"{ApiBase}/customization/anonymous-donor/{EMAOrganizationID}", content);
        }
        catch { }
    }

    // ---- Entity Aliases ----
    public async Task<List<EntityAliasItem>> GetEntityAliasesAsync(int orgID)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<List<EntityAliasItem>>($"{ApiBase}/customization/entity-aliases/{orgID}");
            return result ?? new();
        }
        catch { return new(); }
    }

    public async Task UpsertEntityAliasAsync(int orgID, EntityAliasItem item)
    {
        try
        {
            var payload = new { EntityName = item.EntityName, AliasName = item.AliasName };
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            await _http.PutAsync($"{ApiBase}/customization/entity-aliases/{orgID}", content);
        }
        catch { }
    }

    // ---- Interaction Types ----
    public async Task<List<InteractionTypeItem>> GetInteractionTypesAsync(int orgID, bool activeOnly = false)
    {
        try
        {
            var url = activeOnly ? $"{ApiBase}/customization/interaction-types/{orgID}?activeOnly=true" : $"{ApiBase}/customization/interaction-types/{orgID}";
            var result = await _http.GetFromJsonAsync<List<InteractionTypeItem>>(url);
            return result ?? new();
        }
        catch { return new(); }
    }

    public async Task UpdateInteractionTypeAsync(InteractionTypeItem item)
    {
        try
        {
            var payload = new { Description = item.Description, IsActive = item.IsActive ? "Y" : "N", Sequence = item.Sequence };
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            await _http.PutAsync($"{ApiBase}/customization/interaction-types/{item.EMAInteractionTypeID}", content);
        }
        catch { }
    }

    public async Task SetInteractionTypeActiveAsync(int id, bool isActive)
    {
        try
        {
            var payload = new { IsActive = isActive ? "Y" : "N" };
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            await _http.PutAsync($"{ApiBase}/customization/interaction-types/{id}", content);
        }
        catch { }
    }

    public async Task<int> InsertInteractionTypeAsync(int orgID, string description)
    {
        try
        {
            var payload = new { EMAOrganizationID = orgID, Description = description };
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{ApiBase}/customization/interaction-types", content);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                return result.GetProperty("EMAInteractionTypeID").GetInt32();
            }
        }
        catch { }
        return 0;
    }

    // ---- Donor Status ---- (IsActive is a real bit — serialized as a JSON bool, not "Y"/"N")
    public async Task<List<DonorStatusItem>> GetDonorStatusesAsync(int orgID, bool activeOnly = false)
    {
        try
        {
            var url = activeOnly ? $"{ApiBase}/customization/donor-statuses/{orgID}?activeOnly=true" : $"{ApiBase}/customization/donor-statuses/{orgID}";
            var result = await _http.GetFromJsonAsync<List<DonorStatusItem>>(url);
            return result ?? new();
        }
        catch { return new(); }
    }

    public async Task UpdateDonorStatusAsync(DonorStatusItem item)
    {
        try
        {
            var payload = new { Description = item.Description, IsActive = item.IsActive, Sequence = item.Sequence };
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            await _http.PutAsync($"{ApiBase}/customization/donor-statuses/{item.DonorStatusID}", content);
        }
        catch { }
    }

    public async Task<int> InsertDonorStatusAsync(int orgID, string description)
    {
        try
        {
            var payload = new { EMAOrganizationID = orgID, Description = description };
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{ApiBase}/customization/donor-statuses", content);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                return result.GetProperty("DonorStatusID").GetInt32();
            }
        }
        catch { }
        return 0;
    }

    public async Task<PADCustomizationData?> GetCustomizationDataAsync(int orgID)
    {
        try
        {
            var data = new PADCustomizationData();
            data.Events = await GetEventsAsync(orgID, activeOnly: true);
            data.DonationSources = await GetDonationSourcesAsync(orgID, activeOnly: true);
            data.MethodsOfGiving = await GetMethodsOfGivingAsync(orgID, activeOnly: true);
            data.Funds = await GetFundsAsync(orgID, activeOnly: true);
            data.Campuses = await GetCampusesAsync(orgID, activeOnly: true);

            try
            {
                var orgJson = await _http.GetFromJsonAsync<System.Text.Json.JsonElement>($"{ApiBase}/organization/{orgID}");
                if (orgJson.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    foreach (var prop in orgJson.EnumerateObject())
                    {
                        if (prop.Name.Equals("EMAMethodOfGivingIDForPADs", StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == System.Text.Json.JsonValueKind.Number)
                            data.EMAMethodOfGivingIDForPADs = prop.Value.GetInt32();
                        else if (prop.Name.Equals("EMAFundIDForPADs", StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == System.Text.Json.JsonValueKind.Number)
                            data.EMAFundIDForPADs = prop.Value.GetInt32();
                        else if (prop.Name.Equals("EMAEventIDForPADs", StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == System.Text.Json.JsonValueKind.Number)
                            data.EMAEventIDForPADs = prop.Value.GetInt32();
                        else if (prop.Name.Equals("EMASourceIDForPADs", StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == System.Text.Json.JsonValueKind.Number)
                            data.EMASourceIDForPADs = prop.Value.GetInt32();
                        else if (prop.Name.Equals("EMACampusIDForPADs", StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == System.Text.Json.JsonValueKind.Number)
                            data.EMACampusIDForPADs = prop.Value.GetInt32();
                    }
                }
            }
            catch { }

            return data;
        }
        catch
        {
            return new PADCustomizationData();
        }
    }

    public async Task SavePADDefaultsAsync(int orgID, int? eventID, int? sourceID, int? methodOfGivingID, int? fundID, int? campusID)
    {
        try
        {
            var payload = new
            {
                EMAOrganizationID = orgID,
                EMAEventIDForPADs = eventID,
                EMASourceIDForPADs = sourceID,
                EMAMethodOfGivingIDForPADs = methodOfGivingID,
                EMAFundIDForPADs = fundID,
                EMACampusIDForPADs = campusID
            };
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            await _http.PutAsync($"{ApiBase}/organization/{orgID}/pad-defaults", content);
        }
        catch { }
    }
}