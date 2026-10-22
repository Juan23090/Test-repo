using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EMA.App.Services;

public class TributeReportService : ITributeReportService
{
    private const string ApiBase = "/api";
    private readonly HttpClient _http;

    public TributeReportService(HttpClient http)
    {
        _http = http;
    }

    public async Task<TributeReportResult> GetTributeReportAsync(
        int orgID,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        var url =
            $"{ApiBase}/reports/tributes/{orgID}" +
            $"?startDate={startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}" +
            $"&endDate={endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

        try
        {
            using var response = await _http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorMessage = await TryReadErrorMessageAsync(response, cancellationToken)
                    ?? $"Failed to load report ({(int)response.StatusCode}).";

                return new TributeReportResult
                {
                    IsSuccess = false,
                    ErrorMessage = errorMessage
                };
            }

            var report = await response.Content.ReadFromJsonAsync<List<TributeReportItem>>(cancellationToken: cancellationToken);
            return new TributeReportResult
            {
                IsSuccess = report is not null,
                Data = report,
                ErrorMessage = report is null ? "Received an empty response from the API." : null
            };
        }
        catch
        {
            return new TributeReportResult
            {
                IsSuccess = false,
                ErrorMessage = "Unable to reach the reports API. Confirm the API is running on port 7071."
            };
        }
    }

    public async Task<TributeDonorsResult> GetTributeDonorsAsync(
        int orgID,
        TributeDonorsFilter filter,
        CancellationToken cancellationToken = default)
    {
        var url = $"{ApiBase}/reports/tributes/{orgID}/donors{BuildDonorsQuery(filter)}";

        try
        {
            using var response = await _http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new TributeDonorsResult
                {
                    IsSuccess = false,
                    ErrorMessage = await TryReadErrorMessageAsync(response, cancellationToken)
                        ?? $"Failed to load tribute donors ({(int)response.StatusCode})."
                };
            }

            var data = await response.Content.ReadFromJsonAsync<TributeDonorsData>(cancellationToken: cancellationToken);
            return new TributeDonorsResult
            {
                IsSuccess = data is not null,
                Data = data,
                ErrorMessage = data is null ? "Received an empty response from the API." : null
            };
        }
        catch
        {
            return new TributeDonorsResult
            {
                IsSuccess = false,
                ErrorMessage = "Unable to reach the reports API. Confirm the API is running on port 7071."
            };
        }
    }

    public async Task<TributeDonorsPdfResult> GetTributeDonorsPdfAsync(
        int orgID,
        TributeDonorsFilter filter,
        CancellationToken cancellationToken = default)
    {
        var url = $"{ApiBase}/reports/tributes/{orgID}/donors/pdf{BuildDonorsQuery(filter)}";

        try
        {
            using var response = await _http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new TributeDonorsPdfResult
                {
                    IsSuccess = false,
                    ErrorMessage = await TryReadErrorMessageAsync(response, cancellationToken)
                        ?? $"Failed to generate PDF ({(int)response.StatusCode})."
                };
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (bytes.Length == 0)
            {
                return new TributeDonorsPdfResult
                {
                    IsSuccess = false,
                    ErrorMessage = "API returned an empty PDF file."
                };
            }

            // Content-Disposition is a content header (and needs CORS exposure to be readable);
            // the page falls back to its own name when it isn't.
            var disposition = response.Content.Headers.ContentDisposition;
            var fileName = (disposition?.FileNameStar ?? disposition?.FileName)?.Trim('"');

            return new TributeDonorsPdfResult
            {
                IsSuccess = true,
                FileBytes = bytes,
                FileName = string.IsNullOrWhiteSpace(fileName) ? null : fileName
            };
        }
        catch
        {
            return new TributeDonorsPdfResult
            {
                IsSuccess = false,
                ErrorMessage = "Unable to reach the reports API. Confirm the API is running on port 7071."
            };
        }
    }

    public async Task<TributeReportActionResult> EmailTributeDonorsAsync(
        int orgID,
        TributeDonorsFilter filter,
        string recipientEmail,
        string subject,
        string message,
        CancellationToken cancellationToken = default)
    {
        // Only the filter is sent; the API rebuilds the PDF from its own data.
        var payload = new
        {
            RecipientEmail = recipientEmail,
            Subject = subject,
            Message = message,
            filter.TributeTypeID,
            filter.HasName,
            filter.Name,
            StartDate = filter.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            EndDate = filter.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        };

        try
        {
            using var response = await _http.PostAsJsonAsync(
                $"{ApiBase}/reports/tributes/{orgID}/donors/email",
                payload,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new TributeReportActionResult
                {
                    IsSuccess = false,
                    ErrorMessage = await TryReadErrorMessageAsync(response, cancellationToken)
                        ?? $"Failed to send email ({(int)response.StatusCode})."
                };
            }

            return new TributeReportActionResult { IsSuccess = true };
        }
        catch
        {
            return new TributeReportActionResult
            {
                IsSuccess = false,
                ErrorMessage = "Unable to reach the reports API. Confirm the API is running on port 7071."
            };
        }
    }

    private static string BuildDonorsQuery(TributeDonorsFilter filter)
    {
        var query =
            $"?tributeTypeID={filter.TributeTypeID.ToString(CultureInfo.InvariantCulture)}" +
            $"&hasName={(filter.HasName ? "true" : "false")}" +
            $"&startDate={filter.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}" +
            $"&endDate={filter.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

        return filter.HasName
            ? query + $"&name={Uri.EscapeDataString(filter.Name ?? string.Empty)}"
            : query;
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

    private sealed class ErrorResponse
    {
        public string? Message { get; set; }
    }
}
