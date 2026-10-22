using System.Globalization;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using EMA.App.Models;

namespace EMA.App.Services;

public class EmaStatisticsService : IEmaStatisticsService
{
    private const string ApiBase = "/api";
    private readonly HttpClient _http;

    public EmaStatisticsService(HttpClient http)
    {
        _http = http;
    }

    public async Task<EmaStatisticsResponse> GetStatisticsAsync(
        DateOnly startDate,
        DateOnly endDate,
        string timePeriod,
        string classification,
        string scope,
        string measurement,
        CancellationToken cancellationToken = default)
    {
        var url =
            $"{ApiBase}/statistics" +
            $"?startDate={startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}" +
            $"&endDate={endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}" +
            $"&timePeriod={Uri.EscapeDataString(timePeriod)}" +
            $"&classification={Uri.EscapeDataString(classification)}" +
            $"&scope={Uri.EscapeDataString(scope)}" +
            $"&measurement={Uri.EscapeDataString(measurement)}";

        try
        {
            using var response = await _http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorMessage = await TryReadErrorMessageAsync(response, cancellationToken)
                    ?? $"Failed to load statistics ({(int)response.StatusCode}).";

                return new EmaStatisticsResponse
                {
                    IsSuccess = false,
                    ErrorMessage = errorMessage
                };
            }

            var data = await response.Content.ReadFromJsonAsync<EmaStatisticsResult>(cancellationToken: cancellationToken);
            return new EmaStatisticsResponse
            {
                IsSuccess = data is not null,
                Data = data,
                ErrorMessage = data is null ? "Received an empty response from the API." : null
            };
        }
        catch
        {
            return new EmaStatisticsResponse
            {
                IsSuccess = false,
                ErrorMessage = "Unable to reach the statistics API. Confirm the API is running on port 7071."
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
