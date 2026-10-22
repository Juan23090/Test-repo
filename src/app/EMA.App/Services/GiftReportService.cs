using System.Globalization;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using EMA.App.Models;

namespace EMA.App.Services;

public class GiftReportService : IGiftReportService
{
    private const string ApiBase = "/api";
    private readonly HttpClient _http;

    public GiftReportService(HttpClient http)
    {
        _http = http;
    }

    public async Task<GiftReportResponse> GetGiftReportAsync(
        int orgID,
        DateOnly startDate,
        DateOnly endDate,
        string timePeriod,
        string classification,
        string metric,
        CancellationToken cancellationToken = default)
    {
        var url =
            $"{ApiBase}/reports/gifts/{orgID}" +
            $"?startDate={startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}" +
            $"&endDate={endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}" +
            $"&timePeriod={Uri.EscapeDataString(timePeriod)}" +
            $"&classification={Uri.EscapeDataString(classification)}" +
            $"&metric={Uri.EscapeDataString(metric)}";

        try
        {
            using var response = await _http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorMessage = await TryReadErrorMessageAsync(response, cancellationToken)
                    ?? $"Failed to load report ({(int)response.StatusCode}).";

                return new GiftReportResponse
                {
                    IsSuccess = false,
                    ErrorMessage = errorMessage
                };
            }

            var report = await response.Content.ReadFromJsonAsync<GiftReportResult>(cancellationToken: cancellationToken);
            return new GiftReportResponse
            {
                IsSuccess = report is not null,
                Data = report,
                ErrorMessage = report is null ? "Received an empty response from the API." : null
            };
        }
        catch
        {
            return new GiftReportResponse
            {
                IsSuccess = false,
                ErrorMessage = "Unable to reach the reports API. Confirm the API is running on port 7071."
            };
        }
    }

    private static async Task<string?> TryReadErrorMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var payload = await response.Content.ReadFromJsonAsync<ErrorResponse>(cancellationToken: cancellationToken);
            return string.IsNullOrWhiteSpace(payload?.Message) ? null : payload.Message;
        }
        catch
        {
            return null;
        }
    }

    private sealed class ErrorResponse
    {
        public string? Message { get; set; }
    }
}
