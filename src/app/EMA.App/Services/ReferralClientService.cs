using System.Net;
using System.Net.Http.Json;
using EMA.App.Models;
using Microsoft.Extensions.Logging;

namespace EMA.App.Services;

public sealed class ReferralClientService : IReferralClientService
{
    private const string ApiBase = "/api";

    private readonly HttpClient _http;
    private readonly ILogger<ReferralClientService> _logger;

    public ReferralClientService(
        HttpClient http,
        ILogger<ReferralClientService> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<ReferralEligibilityDto?> GetEligibilityAsync(
        int orgID,
        CancellationToken cancellationToken = default)
    {
        var route = $"{ApiBase}/organization/{orgID}/referrals/eligibility";

        try
        {
            return await _http.GetFromJsonAsync<ReferralEligibilityDto>(route, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or NotSupportedException or System.Text.Json.JsonException)
        {
            _logger.LogError(ex, "Failed to load referral eligibility for organization {OrgID}", orgID);
            return null;
        }
    }

    public async Task<SendReferralsResult?> SendBatchReferralsAsync(
        int orgID,
        SendReferralsRequest request,
        CancellationToken cancellationToken = default)
    {
        var route = $"{ApiBase}/organization/{orgID}/referrals/send";

        try
        {
            var response = await _http.PostAsJsonAsync(route, request, cancellationToken);

            // A rejected batch still carries the per-recipient reasons the
            // screen needs, so the body is read on failure as well as success.
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.BadRequest)
            {
                return await response.Content.ReadFromJsonAsync<SendReferralsResult>(
                    cancellationToken: cancellationToken);
            }

            _logger.LogError(
                "Sending referrals for organization {OrgID} failed with status {StatusCode}",
                orgID,
                (int)response.StatusCode);

            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or NotSupportedException or System.Text.Json.JsonException)
        {
            _logger.LogError(ex, "Failed to send referrals for organization {OrgID}", orgID);
            return null;
        }
    }

    public async Task<AcceptReferralResult?> AcceptReferralAsync(
        int orgID,
        AcceptReferralRequest request,
        CancellationToken cancellationToken = default)
    {
        var route = $"{ApiBase}/organization/{orgID}/referrals/accept";

        try
        {
            var response = await _http.PostAsJsonAsync(route, request, cancellationToken);

            // The API returns the populated result with a 400 when a referral
            // cannot be claimed, and that message is what the screen shows.
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.BadRequest)
            {
                return await response.Content.ReadFromJsonAsync<AcceptReferralResult>(
                    cancellationToken: cancellationToken);
            }

            _logger.LogError(
                "Accepting a referral for organization {OrgID} failed with status {StatusCode}",
                orgID,
                (int)response.StatusCode);

            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or NotSupportedException or System.Text.Json.JsonException)
        {
            _logger.LogError(ex, "Failed to accept a referral for organization {OrgID}", orgID);
            return null;
        }
    }

    public async Task<ReferralSummaryDto?> GetReferralHistoryAsync(
        int orgID,
        CancellationToken cancellationToken = default)
    {
        var route = $"{ApiBase}/organization/{orgID}/referrals/history";

        try
        {
            return await _http.GetFromJsonAsync<ReferralSummaryDto>(route, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or NotSupportedException or System.Text.Json.JsonException)
        {
            _logger.LogError(ex, "Failed to load referral history for organization {OrgID}", orgID);
            return null;
        }
    }
}
