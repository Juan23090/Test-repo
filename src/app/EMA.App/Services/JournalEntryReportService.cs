using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace EMA.App.Services;

public class JournalEntryReportService : IJournalEntryReportService
{
    private const string ApiBase = "/api";
    private readonly HttpClient _http;

    public JournalEntryReportService(HttpClient http)
    {
        _http = http;
    }

    public async Task<JournalEntryReportResult> GetJournalEntryDetailsAsync(
        int orgID,
        DateOnly startDate,
        DateOnly endDate,
        string sortBy,
        bool jeByDate,
        CancellationToken cancellationToken = default)
    {
        var normalizedSortBy = sortBy?.Trim().ToLowerInvariant() == "account"
            ? "account"
            : "description";

        var url =
            $"{ApiBase}/reports/journal-entries/{orgID}" +
            $"?startDate={startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}" +
            $"&endDate={endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}" +
            $"&sortBy={normalizedSortBy}" +
            $"&jeByDate={jeByDate.ToString().ToLowerInvariant()}";

        try
        {
            using var response = await _http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorMessage = await TryReadErrorMessageAsync(response, cancellationToken)
                    ?? $"Failed to load report ({(int)response.StatusCode}).";

                return new JournalEntryReportResult
                {
                    IsSuccess = false,
                    ErrorMessage = errorMessage
                };
            }

            var report = await response.Content.ReadFromJsonAsync<JournalEntryReportResponse>(cancellationToken: cancellationToken);
            return new JournalEntryReportResult
            {
                IsSuccess = report is not null,
                Data = report,
                ErrorMessage = report is null ? "Received an empty response from the API." : null
            };
        }
        catch
        {
            return new JournalEntryReportResult
            {
                IsSuccess = false,
                ErrorMessage = "Unable to reach the reports API. Confirm the API is running on port 7071."
            };
        }
    }

    public async Task<JournalEntryPdfResult> GetJournalEntryPdfAsync(
        int orgID,
        DateOnly startDate,
        DateOnly endDate,
        string sortBy,
        bool jeByDate,
        CancellationToken cancellationToken = default)
    {
        var normalizedSortBy = sortBy?.Trim().ToLowerInvariant() == "account"
            ? "account"
            : "description";

        var url =
            $"{ApiBase}/reports/journal-entries/{orgID}/pdf" +
            $"?startDate={startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}" +
            $"&endDate={endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}" +
            $"&sortBy={normalizedSortBy}" +
            $"&jeByDate={jeByDate.ToString().ToLowerInvariant()}";

        try
        {
            using var response = await _http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorMessage = await TryReadErrorMessageAsync(response, cancellationToken)
                    ?? $"Failed to generate PDF ({(int)response.StatusCode}).";

                return new JournalEntryPdfResult
                {
                    IsSuccess = false,
                    ErrorMessage = errorMessage
                };
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (bytes.Length == 0)
            {
                return new JournalEntryPdfResult
                {
                    IsSuccess = false,
                    ErrorMessage = "API returned an empty PDF file."
                };
            }

            return new JournalEntryPdfResult
            {
                IsSuccess = true,
                FileBytes = bytes,
                FileName = GetFileNameFromResponse(response)
            };
        }
        catch
        {
            return new JournalEntryPdfResult
            {
                IsSuccess = false,
                ErrorMessage = "Unable to reach the reports API. Confirm the API is running on port 7071."
            };
        }
    }

    public async Task<JournalEntryCsvResult> GetJournalEntryCsvAsync(
        int orgID,
        DateOnly startDate,
        DateOnly endDate,
        string sortBy,
        bool jeByDate,
        CancellationToken cancellationToken = default)
    {
        var normalizedSortBy = sortBy?.Trim().ToLowerInvariant() == "account"
            ? "account"
            : "description";

        var url =
            $"{ApiBase}/reports/journal-entries/{orgID}/csv" +
            $"?startDate={startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}" +
            $"&endDate={endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}" +
            $"&sortBy={normalizedSortBy}" +
            $"&jeByDate={jeByDate.ToString().ToLowerInvariant()}";

        try
        {
            using var response = await _http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorMessage = await TryReadErrorMessageAsync(response, cancellationToken)
                    ?? $"Failed to generate CSV ({(int)response.StatusCode}).";

                return new JournalEntryCsvResult
                {
                    IsSuccess = false,
                    ErrorMessage = errorMessage
                };
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (bytes.Length == 0)
            {
                return new JournalEntryCsvResult
                {
                    IsSuccess = false,
                    ErrorMessage = "API returned an empty CSV file."
                };
            }

            return new JournalEntryCsvResult
            {
                IsSuccess = true,
                FileBytes = bytes,
                FileName = GetFileNameFromResponse(response)
            };
        }
        catch
        {
            return new JournalEntryCsvResult
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
        catch (JsonException)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string? GetFileNameFromResponse(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Content-Disposition", out var values))
            return null;

        var header = values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(header))
            return null;

        const string fileNameToken = "filename=";
        var index = header.IndexOf(fileNameToken, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return null;

        var value = header[(index + fileNameToken.Length)..].Trim();
        if (value.StartsWith("\"", StringComparison.Ordinal) && value.EndsWith("\"", StringComparison.Ordinal) && value.Length > 1)
        {
            value = value[1..^1];
        }

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private sealed class ErrorResponse
    {
        public string? Message { get; set; }
    }
}
