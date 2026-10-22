using System.Net.Http.Json;
using EMA.App.Models;

namespace EMA.App.Services;

public sealed class ScreenFeedbackService : IScreenFeedbackService
{
    private readonly HttpClient _http;

    public ScreenFeedbackService(HttpClient http)
    {
        _http = http;
    }

    public async Task<ScreenActionSummary> GetScreenActionSummaryAsync(
        int menuItemID,
        CancellationToken cancellationToken = default)
    {
        return await _http.GetFromJsonAsync<ScreenActionSummary>(
                   $"api/screen-actions/{menuItemID}",
                   cancellationToken)
               ?? new ScreenActionSummary
               {
                   MenuItemID = menuItemID
               };
    }

    public Task<int> CreateFaqAsync(
        int menuItemID,
        CreateFaqRequest request,
        CancellationToken cancellationToken = default) =>
        PostForIDAsync(
            $"api/screen-actions/{menuItemID}/faqs",
            request,
            cancellationToken);

    public Task<int> CreateGlobalFaqAsync(
        CreateFaqRequest request,
        CancellationToken cancellationToken = default) =>
        PostForIDAsync(
            "api/insider/faqs",
            request,
            cancellationToken);

    public Task<int> CreateEnhancementRequestAsync(
        int menuItemID,
        CreateEnhancementRequest request,
        CancellationToken cancellationToken = default) =>
        PostForIDAsync(
            $"api/screen-actions/{menuItemID}/enhancements",
            request,
            cancellationToken);

    public Task<int> CreateSupportTicketAsync(
        int menuItemID,
        CreateSupportTicketRequest request,
        CancellationToken cancellationToken = default) =>
        PostForIDAsync(
            $"api/screen-actions/{menuItemID}/support",
            request,
            cancellationToken);

    public async Task SaveEnhancementVoteAsync(
        int enhancementRequestID,
        string vote,
        CancellationToken cancellationToken = default)
    {
        var response = await _http.PutAsJsonAsync(
            $"api/screen-actions/enhancements/{enhancementRequestID}/vote",
            new SaveEnhancementVoteRequest
            {
                Vote = vote
            },
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<List<PublicFaq>> GetPublicFaqsAsync(
        CancellationToken cancellationToken = default) =>
        await _http.GetFromJsonAsync<List<PublicFaq>>(
            "api/public/faqs",
            cancellationToken)
        ?? new List<PublicFaq>();

    public async Task<List<FaqAdminItem>> GetFaqAdminAsync(
        CancellationToken cancellationToken = default) =>
        await _http.GetFromJsonAsync<List<FaqAdminItem>>(
            "api/insider/faqs",
            cancellationToken)
        ?? new List<FaqAdminItem>();

    public async Task<string?> GetPublishedFaqHtmlAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(
            "api/public/faqs/page",
            cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task<PublicFaqPublishResponse> PublishPublicFaqPageAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsync(
            "api/insider/faqs/publish",
            content: null,
            cancellationToken: cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<PublicFaqPublishResponse>(
                   cancellationToken: cancellationToken)
               ?? throw new InvalidOperationException(
                   "EMA did not return the public FAQ publication result.");
    }

    public async Task UpdateFaqAsync(
        int faqID,
        UpdateFaqAdminRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _http.PutAsJsonAsync(
            $"api/insider/faqs/{faqID}",
            request,
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<EnhancementAdminResponse> GetEnhancementAdminAsync(
        CancellationToken cancellationToken = default) =>
        await _http.GetFromJsonAsync<EnhancementAdminResponse>(
            "api/insider/enhancements",
            cancellationToken)
        ?? new EnhancementAdminResponse();

    public async Task UpdateEnhancementAsync(
        int enhancementRequestID,
        UpdateEnhancementAdminRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _http.PutAsJsonAsync(
            $"api/insider/enhancements/{enhancementRequestID}",
            request,
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<SupportTicketAdminResponse> GetSupportTicketAdminAsync(
        CancellationToken cancellationToken = default) =>
        await _http.GetFromJsonAsync<SupportTicketAdminResponse>(
            "api/insider/support-tickets",
            cancellationToken)
        ?? new SupportTicketAdminResponse();

    public async Task UpdateSupportTicketAsync(
        int supportTicketID,
        UpdateSupportTicketAdminRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _http.PutAsJsonAsync(
            $"api/insider/support-tickets/{supportTicketID}",
            request,
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
    }

    private async Task<int> PostForIDAsync<T>(
        string url,
        T request,
        CancellationToken cancellationToken)
    {
        var response = await _http.PostAsJsonAsync(
            url,
            request,
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content
            .ReadFromJsonAsync<CreateScreenFeedbackResponse>(
                cancellationToken: cancellationToken);

        return result?.ID
            ?? throw new InvalidOperationException(
                "EMA did not return the saved record ID.");
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? message = null;

        try
        {
            var error = await response.Content
                .ReadFromJsonAsync<ApiError>(
                    cancellationToken: cancellationToken);

            message = error?.Message;
        }
        catch
        {
            // Fall back to a status-based error below.
        }

        throw new HttpRequestException(
            string.IsNullOrWhiteSpace(message)
                ? $"EMA request failed ({(int)response.StatusCode})."
                : message);
    }

    private sealed class ApiError
    {
        public string? Message { get; set; }
    }
}
