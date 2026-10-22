using System.Net.Http.Json;
using EMA.App.Models;

namespace EMA.App.Services;

public sealed class OrganizationOnboardingService : IOrganizationOnboardingService
{
    private const string ApiBase =
        "/api/organization";

    private readonly HttpClient _http;

    public OrganizationOnboardingService(
        HttpClient http)
    {
        _http = http;
    }

    public async Task<OrganizationOnboardingSettings?> GetAsync(
        int organizationID,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<
                OrganizationOnboardingSettings>(
                $"{ApiBase}/{organizationID}/onboarding/donation-attributes",
                cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<OnboardingSaveResult> SaveAsync(
        int organizationID,
        string section,
        SaveOrganizationOnboardingSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response =
                await _http.PutAsJsonAsync(
                    $"{ApiBase}/{organizationID}/onboarding/donation-attributes/{Uri.EscapeDataString(section)}",
                    request,
                    cancellationToken);

            var message =
                response.IsSuccessStatusCode
                    ? "Your setup has been saved."
                    : $"The request failed with status {(int)response.StatusCode}.";

            try
            {
                var body =
                    await response.Content.ReadFromJsonAsync<
                        ApiMessage>(
                        cancellationToken:
                            cancellationToken);

                if (!string.IsNullOrWhiteSpace(
                        body?.Message))
                {
                    message =
                        body.Message;
                }
            }
            catch
            {
                // Keep the status-code fallback message.
            }

            return new OnboardingSaveResult
            {
                Succeeded =
                    response.IsSuccessStatusCode,

                StatusCode =
                    (int)response.StatusCode,

                Message =
                    message
            };
        }
        catch
        {
            return new OnboardingSaveResult
            {
                Succeeded = false,
                StatusCode = 0,
                Message =
                    "The setup could not be saved. Please try again."
            };
        }
    }

    private sealed class ApiMessage
    {
        public string Message { get; set; } =
            string.Empty;
    }
}
