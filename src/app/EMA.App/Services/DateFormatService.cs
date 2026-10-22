using System.Net.Http.Json;
using EMA.App.Models;

namespace EMA.App.Services;

public class DateFormatService : IDateFormatService
{
    private const string ApiBase = "/api";

    private readonly HttpClient _http;

    public DateFormatService(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<DateFormatSummary>> GetDateFormatsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<DateFormatSummary>>(
                $"{ApiBase}/date-formats",
                cancellationToken) ?? new();
        }
        catch
        {
            return new();
        }
    }
}