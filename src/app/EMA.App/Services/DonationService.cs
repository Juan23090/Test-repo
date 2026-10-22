using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EMA.App.Models;

namespace EMA.App.Services;

public class DonationService : IDonationService
{
    private const string ApiBase = "/api";

    private readonly HttpClient _http;
    private readonly IUserSessionStateService _session;

    public DonationService(
        HttpClient http,
        IUserSessionStateService session)
    {
        _http = http;
        _session = session;
    }

    private int? SessionUserID =>
        _session.CurrentSession?.UserID;

    public async Task<List<DonationItem>> GetAllAsync(
        int orgID,
        CancellationToken cancellationToken = default)
    {
        var response =
            await _http.GetAsync(
                $"{ApiBase}/donations/{orgID}",
                cancellationToken);

        await EnsureSuccessAsync(
            response,
            "Unable to load donations.",
            cancellationToken);

        return await response.Content
                   .ReadFromJsonAsync<List<DonationItem>>(
                       cancellationToken: cancellationToken)
               ?? [];
    }

    public Task<List<DonationItem>> GetByDonorAsync(
        int orgID,
        long donorID,
        CancellationToken cancellationToken = default) =>
        GetFilteredAsync(
            orgID,
            $"donorID={donorID}",
            cancellationToken);

    public Task<List<DonationItem>> GetByDateAsync(
        int orgID,
        DateTime receivedDate,
        CancellationToken cancellationToken = default) =>
        GetFilteredAsync(
            orgID,
            $"receivedDate={receivedDate:yyyy-MM-dd}",
            cancellationToken);

    private async Task<List<DonationItem>> GetFilteredAsync(
        int orgID,
        string query,
        CancellationToken cancellationToken)
    {
        var response =
            await _http.GetAsync(
                $"{ApiBase}/donations/{orgID}?{query}",
                cancellationToken);

        await EnsureSuccessAsync(
            response,
            "Unable to load donations.",
            cancellationToken);

        return await response.Content
                   .ReadFromJsonAsync<List<DonationItem>>(
                       cancellationToken: cancellationToken)
               ?? [];
    }

    public async Task<DonationItem?> GetByIDAsync(
        int orgID,
        long donationID,
        CancellationToken cancellationToken = default)
    {
        var response =
            await _http.GetAsync(
                $"{ApiBase}/donations/{orgID}/{donationID}",
                cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(
            response,
            "Unable to load the donation.",
            cancellationToken);

        return await response.Content
            .ReadFromJsonAsync<DonationItem>(
                cancellationToken: cancellationToken);
    }

    public async Task<long?> CreateAsync(
        int orgID,
        DonationItem donation,
        CancellationToken cancellationToken = default)
    {
        donation.EMAOrganizationID =
            orgID;

        donation.CreatedbyUserID =
            SessionUserID;

        donation.UpdatedbyUserID =
            SessionUserID;

        var response =
            await _http.PostAsJsonAsync(
                $"{ApiBase}/donations/{orgID}",
                donation,
                cancellationToken);

        await EnsureSuccessAsync(
            response,
            "Unable to create the donation.",
            cancellationToken);

        var result =
            await response.Content
                .ReadFromJsonAsync<JsonElement>(
                    cancellationToken: cancellationToken);

        return result.TryGetProperty(
                "donationID",
                out var donationID)
            ? donationID.GetInt64()
            : null;
    }

    public async Task<bool> UpdateAsync(
        int orgID,
        DonationItem donation,
        CancellationToken cancellationToken = default)
    {
        donation.EMAOrganizationID =
            orgID;

        donation.UpdatedbyUserID =
            SessionUserID;

        var response =
            await _http.PutAsJsonAsync(
                $"{ApiBase}/donations/{orgID}/{donation.EMADonationID}",
                donation,
                cancellationToken);

        await EnsureSuccessAsync(
            response,
            "Unable to update the donation.",
            cancellationToken);

        return true;
    }

    public async Task ConfirmAsync(
        long donationID,
        CancellationToken cancellationToken = default)
    {
        var response =
            await _http.PostAsync(
                $"{ApiBase}/email/confirm/{donationID}",
                null,
                cancellationToken);

        await EnsureSuccessAsync(
            response,
            "Unable to confirm the donation.",
            cancellationToken);
    }

    public async Task AcknowledgeAsync(
        long donationID,
        CancellationToken cancellationToken = default)
    {
        var response =
            await _http.PostAsync(
                $"{ApiBase}/email/acknowledge/{donationID}",
                null,
                cancellationToken);

        await EnsureSuccessAsync(
            response,
            "Unable to acknowledge the donation.",
            cancellationToken);
    }

    public async Task EmailSaveAsync(
        long donationID,
        object payload,
        CancellationToken cancellationToken = default)
    {
        var response =
            await _http.PostAsJsonAsync(
                $"{ApiBase}/email/save/{donationID}",
                payload,
                cancellationToken);

        await EnsureSuccessAsync(
            response,
            "Unable to save the email donation changes.",
            cancellationToken);
    }

    public async Task<List<JsonElement>> GetDonationAuditAsync(
        int orgID,
        long donationID,
        CancellationToken cancellationToken = default)
    {
        var response =
            await _http.GetAsync(
                $"{ApiBase}/donations/{orgID}/{donationID}/audit",
                cancellationToken);

        await EnsureSuccessAsync(
            response,
            "Unable to load donation history.",
            cancellationToken);

        return await response.Content
                   .ReadFromJsonAsync<List<JsonElement>>(
                       cancellationToken: cancellationToken)
               ?? [];
    }

    public async Task<List<JsonElement>> GetDonationStepLogAsync(
        long donationID,
        CancellationToken cancellationToken = default)
    {
        var response =
            await _http.GetAsync(
                $"{ApiBase}/email/steps/{donationID}",
                cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        await EnsureSuccessAsync(
            response,
            "Unable to load donation steps.",
            cancellationToken);

        return await response.Content
                   .ReadFromJsonAsync<List<JsonElement>>(
                       cancellationToken: cancellationToken)
               ?? [];
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string fallbackMessage,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var message =
            fallbackMessage;

        try
        {
            var error =
                await response.Content
                    .ReadFromJsonAsync<ApiErrorResponse>(
                        cancellationToken: cancellationToken);

            if (!string.IsNullOrWhiteSpace(
                    error?.Message))
            {
                message =
                    error.Message;
            }
        }
        catch
        {
            // Use the operation-specific fallback message.
        }

        throw new InvalidOperationException(
            message);
    }

    private sealed class ApiErrorResponse
    {
        public string? Message { get; init; }
    }
}