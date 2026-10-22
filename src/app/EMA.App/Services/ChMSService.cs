using System.Net.Http.Json;
using System.Text.Json;
using EMA.App.Models;

namespace EMA.App.Services;

public class ChMSService : IChMSService
{
    private const string ApiBase = "/api";
    private readonly HttpClient _http;

    public ChMSService(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<ChMSSummary>> GetChMSAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<ChMSSummary>>(
                $"{ApiBase}/chms",
                cancellationToken) ?? new List<ChMSSummary>();
        }
        catch
        {
            return new List<ChMSSummary>();
        }
    }

    public async Task<ChMSLookups> GetLookupsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<ChMSLookups>(
                $"{ApiBase}/chms/lookups",
                cancellationToken) ?? new ChMSLookups();
        }
        catch
        {
            return new ChMSLookups();
        }
    }

    public async Task<int?> InsertChMSAsync(ChMSSummary chms, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync($"{ApiBase}/chms", chms, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            var result = await response.Content.ReadFromJsonAsync<InsertResult>(cancellationToken);
            return result?.ChMSID;
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> UpdateChMSAsync(ChMSSummary chms, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.PutAsJsonAsync(
                $"{ApiBase}/chms/{chms.ChMSID}", chms, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private sealed class InsertResult
    {
        public int ChMSID { get; set; }
    }
}
