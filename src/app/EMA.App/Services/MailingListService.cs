using EMA.App.Models;
using System.Net.Http.Json;

namespace EMA.App.Services;

public class MailingListService : IMailingListService
{
    private readonly HttpClient _http;
    private const string ApiBase = "/api";

    public MailingListService(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<MailingListItem>> GetMailingListsAsync(int orgID, bool showInactive = false)
    {
        try
        {
            string url = $"{ApiBase}/mailing-lists/{orgID}?showInactive={showInactive.ToString().ToLowerInvariant()}";
            var res = await _http.GetFromJsonAsync<List<MailingListItem>>(url);
            return res ?? new List<MailingListItem>();
        }
        catch
        {
            return new List<MailingListItem>();
        }
    }

    public async Task<MailingListDetailsModel?> GetMailingListDetailsAsync(int orgID, int listID)
    {
        try
        {
            return await _http.GetFromJsonAsync<MailingListDetailsModel>($"{ApiBase}/mailing-lists/{orgID}/{listID}");
        }
        catch
        {
            return null;
        }
    }

    public async Task<int> PrepareMailingListAsync(int orgID, int listID)
    {
        var response = await _http.PostAsync($"{ApiBase}/mailing-lists/{orgID}/{listID}/prepare", null);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<PrepareResult>();
        return json?.InstanceID ?? 0;
    }

    public async Task<byte[]> ExportMailingListInstanceAsync(int orgID, int instanceID)
    {
        using var response = await _http.GetAsync($"{ApiBase}/mailing-lists/{orgID}/instance/{instanceID}/export");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync();
    }

    public async Task<int> CreateMailingListAsync(int orgID, CreateMailingListModel model)
    {
        var response = await _http.PostAsJsonAsync($"{ApiBase}/mailing-lists/{orgID}", model);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<CreateResult>();
        return json?.MailingListID ?? 0;
    }

    public async Task UpdateMailingListAsync(int orgID, int listID, CreateMailingListModel model)
    {
        var response = await _http.PutAsJsonAsync($"{ApiBase}/mailing-lists/{orgID}/{listID}", model);
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<ReviewDonorItem>> GetReviewDonorsAsync(int orgID, int listID, string? search = null)
    {
        try
        {
            string url = $"{ApiBase}/mailing-lists/{orgID}/{listID}/review";
            if (!string.IsNullOrWhiteSpace(search))
            {
                url += $"?search={Uri.EscapeDataString(search)}";
            }
            var res = await _http.GetFromJsonAsync<List<ReviewDonorItem>>(url);
            return res ?? new List<ReviewDonorItem>();
        }
        catch
        {
            return new List<ReviewDonorItem>();
        }
    }

    public async Task ToggleDonorOptStatusAsync(int orgID, int listID, int donorID, bool? optedIn, bool? optedOut)
    {
        var body = new { optedIn, optedOut };
        var response = await _http.PostAsJsonAsync($"{ApiBase}/mailing-lists/{orgID}/{listID}/donors/{donorID}/toggle-opt", body);
        response.EnsureSuccessStatusCode();
    }

    private record PrepareResult(int InstanceID, string Message);
    private record CreateResult(int MailingListID, string Message);
}
