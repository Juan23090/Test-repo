using System.Net.Http.Json;
using EMA.App.Models;

namespace EMA.App.Services;

public sealed class PublicOnboardingService : IPublicOnboardingService
{
    private const string ApiBase = "/api/public/onboarding";
    private readonly HttpClient _http;

    public PublicOnboardingService(HttpClient http)
    {
        _http = http;
    }

    public async Task<PublicOnboardingLookups?> GetLookupsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<PublicOnboardingLookups>(
                $"{ApiBase}/lookups",
                cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<PublicOnboardingSubmitResult> SubmitAsync(
        PublicOrganizationRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync(
                $"{ApiBase}/organization",
                request,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var created = await response.Content.ReadFromJsonAsync<PublicOrganizationRequestCreated>(
                    cancellationToken: cancellationToken);

                return new PublicOnboardingSubmitResult
                {
                    Succeeded = true,
                    StatusCode = (int)response.StatusCode,
                    EMAOrganizationID = created?.EMAOrganizationID,
                    Message = created?.Message ?? "Your request has been submitted."
                };
            }

            ApiError? error = null;

            try
            {
                error = await response.Content.ReadFromJsonAsync<ApiError>(
                    cancellationToken: cancellationToken);
            }
            catch
            {
                // The status fallback below is still useful.
            }

            return new PublicOnboardingSubmitResult
            {
                Succeeded = false,
                StatusCode = (int)response.StatusCode,
                Message = string.IsNullOrWhiteSpace(error?.Message)
                    ? $"The request failed with status {(int)response.StatusCode}."
                    : error.Message
            };
        }
        catch
        {
            return new PublicOnboardingSubmitResult
            {
                Succeeded = false,
                StatusCode = 0,
                Message = "The request could not be submitted. Please try again."
            };
        }
    }

    private sealed class ApiError
    {
        public string Message { get; set; } = string.Empty;
    }
}
