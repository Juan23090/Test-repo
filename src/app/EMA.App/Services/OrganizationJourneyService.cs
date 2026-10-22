using System.Net.Http.Json;
using EMA.App.Models;

namespace EMA.App.Services;

public class OrganizationJourneyService : IOrganizationJourneyService
{
    private readonly HttpClient _http;
    private const string ApiBase = "api";

    public OrganizationJourneyService(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<OrganizationJourneyEntry>> GetJourneyHistoryAsync(int orgID)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<List<OrganizationJourneyEntry>>($"{ApiBase}/organization/journey-history/{orgID}");
            return result ?? new List<OrganizationJourneyEntry>();
        }
        catch
        {
            return new List<OrganizationJourneyEntry>();
        }
    }

    public async Task<OrganizationJourneyFeedbackSummary> GetJourneyFeedbackAsync(int orgID)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<OrganizationJourneyFeedbackSummary>($"{ApiBase}/organization/journey-feedback/{orgID}");
            return result ?? new OrganizationJourneyFeedbackSummary();
        }
        catch
        {
            return new OrganizationJourneyFeedbackSummary();
        }
    }

    public async Task<int?> CreateJourneyEntryAsync(int orgID, OrganizationJourneyEntry entry)
    {
        try
        {
            var response = await _http.PostAsJsonAsync($"{ApiBase}/organization/journey-history/{orgID}", entry);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<CreateJourneyResult>();
                return result?.OrganizationInteractionID;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> UpdateOrganizationDetailsAsync(int orgID, OrganizationSummary orgDetails)
    {
        try
        {
            var response = await _http.PutAsJsonAsync($"{ApiBase}/organization/journey-update/{orgID}", orgDetails);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<OrganizationJourneyActivityType>> GetJourneyActivityTypesAsync()
    {
        try
        {
            var result = await _http.GetFromJsonAsync<List<OrganizationJourneyActivityType>>($"{ApiBase}/organization/journey-activity-types");
            return result ?? new List<OrganizationJourneyActivityType>();
        }
        catch
        {
            return new List<OrganizationJourneyActivityType>();
        }
    }

    public async Task<List<OrganizationFollowUpItem>> GetFollowUpQueueAsync()
    {
        try
        {
            var result = await _http.GetFromJsonAsync<List<OrganizationFollowUpItem>>($"{ApiBase}/organization/followups");
            return result ?? new List<OrganizationFollowUpItem>();
        }
        catch
        {
            return new List<OrganizationFollowUpItem>();
        }
    }

    public async Task<List<OrganizationStatusItem>> GetOrganizationStatusesAsync()
    {
        try
        {
            var result = await _http.GetFromJsonAsync<List<OrganizationStatusItem>>($"{ApiBase}/organization/statuses");
            return result ?? new List<OrganizationStatusItem>();
        }
        catch
        {
            return new List<OrganizationStatusItem>();
        }
    }

    public async Task<List<OrganizationSummary>> GetAllOrganizationsAsync()
    {
        try
        {
            var result = await _http.GetFromJsonAsync<List<OrganizationSummary>>($"{ApiBase}/organization/all");
            return result ?? new List<OrganizationSummary>();
        }
        catch
        {
            return new List<OrganizationSummary>();
        }
    }

    public async Task<List<ChMSOption>> GetChMSListAsync()
    {
        try
        {
            var result = await _http.GetFromJsonAsync<List<ChMSOption>>($"{ApiBase}/savings-calculator/chms-options");
            return result ?? new List<ChMSOption>();
        }
        catch
        {
            return new List<ChMSOption>();
        }
    }

    public async Task<List<AccountingSoftwareSummary>> GetAccountingSoftwareAsync()
    {
        try
        {
            var result = await _http.GetFromJsonAsync<List<AccountingSoftwareSummary>>($"{ApiBase}/accounting-software");
            return result ?? new List<AccountingSoftwareSummary>();
        }
        catch
        {
            return new List<AccountingSoftwareSummary>();
        }
    }

    private class CreateJourneyResult
    {
        public int OrganizationInteractionID { get; set; }
    }
}
