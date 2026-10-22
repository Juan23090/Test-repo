using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EMA.App.Models;
using Microsoft.JSInterop;

namespace EMA.App.Services;

public sealed class SandboxService : ISandboxService
{
    private const string ApiBase = "/api/sandbox";
    private const string CapabilityHeaderName =
        "X-Sandbox-Capability";
    private const string CapabilityStorageKey =
        "ema-sandbox-capability";

    private readonly HttpClient _http;
    private readonly IJSRuntime _jsRuntime;

    private readonly JsonSerializerOptions _jsonOptions =
        new(JsonSerializerDefaults.Web);

    public SandboxService(
        HttpClient http,
        IJSRuntime jsRuntime)
    {
        _http = http;
        _jsRuntime = jsRuntime;
    }

    public async Task<SandboxServiceResult<CurrentSandboxResponse>> GetCurrentSandboxAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var capability =
                await GetStoredCapabilityAsync();

            using var request =
                new HttpRequestMessage(
                    HttpMethod.Get,
                    $"{ApiBase}/current");

            if (!string.IsNullOrWhiteSpace(
                    capability))
            {
                request.Headers
                    .TryAddWithoutValidation(
                        CapabilityHeaderName,
                        capability);
            }

            var response =
                await _http.SendAsync(
                    request,
                    cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // Only remove a stored key when one was actually supplied.
                // A browser without a key can legitimately receive NotFound
                // when no sandbox exists at its current location.
                if (!string.IsNullOrWhiteSpace(
                        capability))
                {
                    await RemoveStoredCapabilityAsync();
                }

                return SandboxServiceResult<CurrentSandboxResponse>.Success(
                    new CurrentSandboxResponse
                    {
                        HasActiveSandbox = false
                    },
                    (int)response.StatusCode);
            }

            return await ReadResultAsync<CurrentSandboxResponse>(
                response,
                cancellationToken);
        }
        catch (Exception exception)
        {
            return SandboxServiceResult<CurrentSandboxResponse>.Failure(
                $"Unable to check for an existing sandbox: {exception.Message}");
        }
    }

    public async Task<SandboxServiceResult<CreateSandboxResponse>> CreateSandboxAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request =
                new HttpRequestMessage(
                    HttpMethod.Post,
                    $"{ApiBase}/create");

            var existingCapability =
                await GetStoredCapabilityAsync();

            if (!string.IsNullOrWhiteSpace(
                    existingCapability))
            {
                request.Headers
                    .TryAddWithoutValidation(
                        CapabilityHeaderName,
                        existingCapability);
            }

            var response =
                await _http.SendAsync(
                    request,
                    cancellationToken);

            var result =
                await ReadResultAsync<CreateSandboxResponse>(
                response,
                cancellationToken);

            if (result.IsSuccess &&
                result.Value is not null &&
                !string.IsNullOrWhiteSpace(
                    result.Value.Capability))
            {
                await StoreCapabilityAsync(
                    result.Value.Capability);
            }

            return result;
        }
        catch (Exception exception)
        {
            return SandboxServiceResult<CreateSandboxResponse>.Failure(
                $"Unable to create the sandbox: {exception.Message}");
        }
    }

    public async Task<SandboxServiceResult<LoginResponse>> ResumeSandboxAsync(
        int sandboxOrganizationID,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var capability =
                await GetStoredCapabilityAsync();

            using var request =
                new HttpRequestMessage(
                    HttpMethod.Post,
                    $"{ApiBase}/resume")
                {
                    Content =
                        JsonContent.Create(
                            new ResumeSandboxRequest
                            {
                                SandboxOrganizationID =
                                    sandboxOrganizationID
                            })
                };

            if (!string.IsNullOrWhiteSpace(
                    capability))
            {
                request.Headers.TryAddWithoutValidation(
                    CapabilityHeaderName,
                    capability);
            }

            var response =
                await _http.SendAsync(
                    request,
                    cancellationToken);

            return await ReadResultAsync<LoginResponse>(
                response,
                cancellationToken);
        }
        catch (Exception exception)
        {
            return SandboxServiceResult<LoginResponse>.Failure(
                $"Unable to resume the sandbox: {exception.Message}");
        }
    }

    public async Task<SandboxServiceResult<SandboxTestEmailDraft>> GetTestEmailDraftAsync(
        int sandboxOrganizationID,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var capability =
                await GetStoredCapabilityAsync();

            using var request =
                new HttpRequestMessage(
                    HttpMethod.Get,
                    $"{ApiBase}/{sandboxOrganizationID}/test-email");

            if (!string.IsNullOrWhiteSpace(
                    capability))
            {
                request.Headers.TryAddWithoutValidation(
                    CapabilityHeaderName,
                    capability);
            }

            var response =
                await _http.SendAsync(
                    request,
                    cancellationToken);

            return await ReadResultAsync<SandboxTestEmailDraft>(
                response,
                cancellationToken);
        }
        catch (Exception exception)
        {
            return SandboxServiceResult<SandboxTestEmailDraft>.Failure(
                $"Unable to load the sandbox test email: {exception.Message}");
        }
    }

    public async Task<SandboxServiceResult<SendSandboxTestEmailResponse>> SendTestEmailAsync(
        SendSandboxTestEmailRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var capability =
                await GetStoredCapabilityAsync();

            using var httpRequest =
                new HttpRequestMessage(
                    HttpMethod.Post,
                    $"{ApiBase}/{request.SandboxOrganizationID}/test-email/send")
                {
                    Content =
                        JsonContent.Create(
                            request)
                };

            if (!string.IsNullOrWhiteSpace(
                    capability))
            {
                httpRequest.Headers.TryAddWithoutValidation(
                    CapabilityHeaderName,
                    capability);
            }

            var response =
                await _http.SendAsync(
                    httpRequest,
                    cancellationToken);

            return await ReadResultAsync<SendSandboxTestEmailResponse>(
                response,
                cancellationToken);
        }
        catch (Exception exception)
        {
            return SandboxServiceResult<SendSandboxTestEmailResponse>.Failure(
                $"Unable to send the sandbox test email: {exception.Message}");
        }
    }

    private async Task<SandboxServiceResult<T>> ReadResultAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            var value =
                await response.Content.ReadFromJsonAsync<T>(
                    _jsonOptions,
                    cancellationToken);

            return value is null
                ? SandboxServiceResult<T>.Failure(
                    "The sandbox service returned an empty response.",
                    (int)response.StatusCode)
                : SandboxServiceResult<T>.Success(
                    value,
                    (int)response.StatusCode);
        }

        string? errorMessage = null;

        try
        {
            var error =
                await response.Content.ReadFromJsonAsync<SandboxApiError>(
                    _jsonOptions,
                    cancellationToken);

            errorMessage = error?.Message;
        }
        catch
        {
            // Fall back to the HTTP reason phrase below.
        }

        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            errorMessage =
                response.ReasonPhrase ??
                "The sandbox request could not be completed.";
        }

        return SandboxServiceResult<T>.Failure(
            errorMessage,
            (int)response.StatusCode);
    }

    private async Task<string?>
        GetStoredCapabilityAsync()
    {
        try
        {
            return await _jsRuntime
                .InvokeAsync<string?>(
                    "localStorage.getItem",
                    CapabilityStorageKey);
        }
        catch
        {
            return null;
        }
    }

    private async Task StoreCapabilityAsync(
        string capability)
    {
        await _jsRuntime.InvokeVoidAsync(
            "localStorage.setItem",
            CapabilityStorageKey,
            capability);
    }

    private async Task
        RemoveStoredCapabilityAsync()
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync(
                "localStorage.removeItem",
                CapabilityStorageKey);
        }
        catch
        {
            // A blocked storage API should not expose or resume a sandbox.
        }
    }
}
