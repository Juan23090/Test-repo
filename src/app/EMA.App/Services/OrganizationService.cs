using System.Net.Http.Json;
using System.Text.Json;
using EMA.App.Models;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace EMA.App.Services;

public class OrganizationService : IOrganizationService
{
    private const string ApiBase = "/api";

    private readonly HttpClient _http;

    public OrganizationService(HttpClient http)
    {
        _http = http;
    }

    public async Task<OrganizationSummary?> GetOrganizationAsync(
        int orgID,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<OrganizationSummary>(
                $"{ApiBase}/organization/{orgID}",
                cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<OrganizationAuditObject>?> GetOrganizationAuditsAsync(
        int orgID,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.GetFromJsonAsync<List<OrganizationAuditObject>>(
                $"{ApiBase}/organization/{orgID}/audits",
                cancellationToken);

            return response is { Count: > 0 } ? response : null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> PostOrganizationAuditAsync(
        int orgID,
        OrganizationSummary payload,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync(
                $"{ApiBase}/organization/{orgID}/audits",
                payload,
                cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<JeSettings?> GetJeSettingsAsync(
        int orgID,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<JeSettings>(
                $"{ApiBase}/organization/{orgID}/je-settings",
                cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> UpdateJeSettingsAsync(
        int orgID,
        string jeSummarizeBy,
        string jeByDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.PutAsJsonAsync(
                $"{ApiBase}/organization/{orgID}/je-settings",
                new
                {
                    JESummarizeBy = jeSummarizeBy,
                    JEByDate = jeByDate
                },
                cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> UpdateOrganizationAsync(
        int orgID,
        OrganizationSummary organization,
        CancellationToken cancellationToken = default)
    {
        var result =
            await UpdateOrganizationWithResultAsync(
                orgID,
                organization,
                cancellationToken);

        return result.Succeeded;
    }

    public async Task<OrganizationUpdateResult> UpdateOrganizationWithResultAsync(
        int orgID,
        OrganizationSummary organization,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response =
                await _http.PutAsJsonAsync(
                    $"{ApiBase}/organization/{orgID}",
                    organization,
                    new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = null
                    },
                    cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new OrganizationUpdateResult
                {
                    Succeeded = true,
                    StatusCode = (int)response.StatusCode,
                    Message = "Your information has been saved."
                };
            }

            return new OrganizationUpdateResult
            {
                Succeeded = false,
                StatusCode = (int)response.StatusCode,
                Message =
                    await ReadErrorMessageAsync(
                        response,
                        "Failed to save organization.",
                        cancellationToken)
            };
        }
        catch
        {
            return new OrganizationUpdateResult
            {
                Succeeded = false,
                StatusCode = 0,
                Message =
                    "The organization could not be saved. Please try again."
            };
        }
    }

    public async Task<EtransferAliasAvailabilityResult> CheckEtransferAliasAvailabilityAsync(
        int orgID,
        string prefix,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var url =
                $"{ApiBase}/organization/{orgID}/etransfer-alias/availability" +
                $"?prefix={Uri.EscapeDataString(prefix)}";

            var response =
                await _http.GetAsync(
                    url,
                    cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result =
                    await response.Content
                        .ReadFromJsonAsync<EtransferAliasAvailabilityResult>(
                            cancellationToken: cancellationToken);

                return result
                    ?? new EtransferAliasAvailabilityResult
                    {
                        CheckSucceeded = false,
                        IsAvailable = false,
                        Message =
                            "The email availability check returned an empty response."
                    };
            }

            return new EtransferAliasAvailabilityResult
            {
                CheckSucceeded = false,
                IsAvailable = false,
                Message =
                    await ReadErrorMessageAsync(
                        response,
                        "The email availability check failed. Please try again.",
                        cancellationToken)
            };
        }
        catch
        {
            return new EtransferAliasAvailabilityResult
            {
                CheckSucceeded = false,
                IsAvailable = false,
                Message =
                    "The email availability check failed. Please try again."
            };
        }
    }

    public async Task<bool> CreateOrganizationAsync(
        OrganizationSummary organization,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync(
                $"{ApiBase}/organization",
                organization,
                cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<UserItem>?> GetUsersAsync(
        int orgID,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<UserItem>>(
                $"{ApiBase}/organization/{orgID}/users",
                cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> SaveUserAsync(
        int orgID,
        UserItem user,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync(
                $"{ApiBase}/organization/{orgID}/users/save",
                user,
                cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<RoleItem>?> GetRolesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<RoleItem>>(
                $"{ApiBase}/roles",
                cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<MenuItemDto>?> GetMenuItemsByUserAsync(
        int orgID,
        int userID,
        int? categoryID = null,
        bool isDonorSession = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var url =
                isDonorSession
                    ? $"{ApiBase}/organization/{orgID}/donors/{userID}/menu-items"
                    : $"{ApiBase}/organization/{orgID}/users/{userID}/menu-items";

            if (categoryID.HasValue)
            {
                url += $"?categoryID={categoryID.Value}";
            }

            return await _http.GetFromJsonAsync<List<MenuItemDto>>(
                url,
                cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<MenuItemDto>?> GetMenuItemsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<MenuItemDto>>(
                $"{ApiBase}/menu-items",
                cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<LookupItem>?> GetMenuItemCategoriesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<LookupItem>>(
                $"{ApiBase}/menu-item-categories",
                cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<LookupItem>?> GetMenuItemRowsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<LookupItem>>(
                $"{ApiBase}/menu-item-rows",
                cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<LookupItem>?> GetMenuItemTypesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<LookupItem>>(
                $"{ApiBase}/menu-item-types",
                cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<MenuItemResourceTypeDto>?> GetMenuItemResourceTypesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<MenuItemResourceTypeDto>>(
                $"{ApiBase}/menu-item-resource-types",
                cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> SaveMenuItemAsync(
        MenuItemDto menuItem,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync(
                $"{ApiBase}/menu-items/save",
                menuItem,
                cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<MenuItemResourceDto>?> SaveMenuItemResourcesAsync(
        int menuItemID,
        IReadOnlyCollection<MenuItemResourceDto> resources,
        CancellationToken cancellationToken = default)
    {
        if (menuItemID <= 0)
        {
            return null;
        }

        try
        {
            var response = await _http.PostAsJsonAsync(
                $"{ApiBase}/menu-items/{menuItemID}/resources",
                new
                {
                    Resources = resources
                },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<List<MenuItemResourceDto>>(
                cancellationToken: cancellationToken)
                ?? new List<MenuItemResourceDto>();
        }
        catch
        {
            return null;
        }
    }

    public async Task<MenuItemAudioGenerationResult> GenerateMenuItemAudioAsync(
        int menuItemID,
        int menuItemRowID,
        CancellationToken cancellationToken = default)
    {
        if (menuItemID <= 0 || menuItemRowID <= 0)
        {
            return new MenuItemAudioGenerationResult
            {
                Success = false,
                Message = "A saved Audio resource is required before a recording can be generated."
            };
        }

        try
        {
            using var response = await _http.PostAsync(
                $"{ApiBase}/menu-items/{menuItemID}/resources/{menuItemRowID}/generate-audio",
                content: null,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var generated =
                    await response.Content.ReadFromJsonAsync<MenuItemAudioGenerationResponse>(
                        cancellationToken: cancellationToken);

                if (!string.IsNullOrWhiteSpace(generated?.BlobID))
                {
                    return new MenuItemAudioGenerationResult
                    {
                        Success = true,
                        BlobID = generated.BlobID.Trim(),
                        Message = "Production recording generated."
                    };
                }

                return new MenuItemAudioGenerationResult
                {
                    Success = false,
                    Message = "The recording was generated but no EMA Blob ID was returned."
                };
            }

            string? message = null;

            try
            {
                var error =
                    await response.Content.ReadFromJsonAsync<MenuItemAudioGenerationError>(
                        cancellationToken: cancellationToken);
                message = error?.Message;
            }
            catch
            {
                // Fall back to a stable UI message when the API returned no JSON body.
            }

            return new MenuItemAudioGenerationResult
            {
                Success = false,
                Message = string.IsNullOrWhiteSpace(message)
                    ? "Unable to generate the production recording."
                    : message
            };
        }
        catch
        {
            return new MenuItemAudioGenerationResult
            {
                Success = false,
                Message = "Unable to generate the production recording."
            };
        }
    }

    private sealed class MenuItemAudioGenerationResponse
    {
        public string BlobID { get; set; } = string.Empty;
    }

    private sealed class MenuItemAudioGenerationError
    {
        public string Message { get; set; } = string.Empty;
    }

    public async Task<bool> LogMenuItemUsageAsync(
        int menuItemID,
        CancellationToken cancellationToken = default)
    {
        if (menuItemID <= 0)
        {
            return false;
        }

        try
        {
            using var request =
                new HttpRequestMessage(
                    HttpMethod.Post,
                    $"{ApiBase}/navigation/menu-item-usage")
                {
                    Content =
                        JsonContent.Create(
                            new
                            {
                                MenuItemID = menuItemID
                            })
                };

            /*
             * The Blazor app and Functions API use different localhost
             * ports in development, so this is a cross-origin fetch.
             * Explicitly include EmaSession so the API can identify the
             * authenticated server session.
             */
            request.SetBrowserRequestCredentials(
                BrowserRequestCredentials.Include);

            using var response =
                await _http.SendAsync(
                    request,
                    cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
    private static async Task<string> ReadErrorMessageAsync(
        HttpResponseMessage response,
        string fallbackMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload =
                await response.Content
                    .ReadFromJsonAsync<ApiMessageResponse>(
                        cancellationToken: cancellationToken);

            return string.IsNullOrWhiteSpace(
                    payload?.Message)
                ? fallbackMessage
                : payload.Message;
        }
        catch
        {
            return fallbackMessage;
        }
    }

    private sealed class ApiMessageResponse
    {
        public string? Message { get; set; }
    }
}