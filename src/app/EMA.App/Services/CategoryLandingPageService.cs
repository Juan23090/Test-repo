using System.Net.Http.Json;
using EMA.App.Models;

namespace EMA.App.Services;

public sealed class CategoryLandingPageService
    : ICategoryLandingPageService
{
    private const string ApiBase = "/api";
    private const int MaxAttempts = 2;

    private static readonly TimeSpan RetryDelay =
        TimeSpan.FromMilliseconds(175);

    private readonly HttpClient _http;

    public CategoryLandingPageService(
        HttpClient http)
    {
        _http = http;
    }

    public async Task<CategoryLandingPageResult>
        GetLandingPageAsync(
            int EMAOrganizationID,
            int userID,
            int categoryID,
            CancellationToken cancellationToken = default)
    {
        try
        {
            var result =
                await GetFromJsonWithRetryAsync<CategoryLandingPageResult>(
                    $"{ApiBase}/organization/{EMAOrganizationID}/users/{userID}/categories/{categoryID}/landing-page",
                    cancellationToken);

            if (result is null)
            {
                return CreateUnavailableResult(
                    categoryID);
            }

            result.Url =
                NormalizeInternalUrl(
                    result.Url);

            return result;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // A landing-page lookup failure is not an authorization denial.
            // Callers keep the current screen and allow the user to retry.
            return CreateUnavailableResult(
                categoryID);
        }
    }

    public async Task<string> GetLandingUrlAsync(
        int EMAOrganizationID,
        int userID,
        int categoryID,
        CancellationToken cancellationToken = default)
    {
        var result =
            await GetLandingPageAsync(
                EMAOrganizationID,
                userID,
                categoryID,
                cancellationToken);

        return result.Url;
    }

    public async Task<RouteAccessResult> CheckRouteAccessAsync(
        int EMAOrganizationID,
        int userID,
        string path,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var normalizedPath =
                NormalizeInternalUrl(path);

            var escapedPath =
                Uri.EscapeDataString(
                    normalizedPath);

            var result =
                await GetFromJsonWithRetryAsync<RouteAccessResult>(
                    $"{ApiBase}/organization/{EMAOrganizationID}/users/{userID}/route-access?path={escapedPath}",
                    cancellationToken);

            return result ?? new RouteAccessResult
            {
                IsAllowed = false,
                Reason =
                    "No route access response was returned."
            };
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Router navigation checks are cancelled whenever a newer
            // navigation starts. Propagate that cancellation so the old
            // request cannot redirect the user to Access Denied.
            throw;
        }
        catch
        {
            return new RouteAccessResult
            {
                IsAllowed = false,
                Reason =
                    "Route access could not be checked."
            };
        }
    }

    public async Task<bool> IsPublicRouteAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var normalizedPath =
                NormalizeInternalUrl(path);

            var escapedPath =
                Uri.EscapeDataString(
                    normalizedPath);

            var result =
                await GetFromJsonWithRetryAsync<RouteAccessResult>(
                    $"{ApiBase}/navigation/public-route-access?path={escapedPath}",
                    cancellationToken);

            return result?.IsAllowed == true;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    public async Task<CategoryLandingPageResult>
        GetDonorLandingPageAsync(
            int EMAOrganizationID,
            int donorID,
            int categoryID,
            CancellationToken cancellationToken = default)
    {
        try
        {
            var result =
                await GetFromJsonWithRetryAsync<CategoryLandingPageResult>(
                    $"{ApiBase}/organization/{EMAOrganizationID}/donors/{donorID}/categories/{categoryID}/landing-page",
                    cancellationToken);

            if (result is null)
            {
                return CreateUnavailableResult(
                    categoryID);
            }

            result.Url =
                NormalizeInternalUrl(
                    result.Url);

            return result;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // A landing-page lookup failure is not an authorization denial.
            // Callers keep the current screen and allow the user to retry.
            return CreateUnavailableResult(
                categoryID);
        }
    }

    public async Task<string> GetDonorLandingUrlAsync(
        int EMAOrganizationID,
        int donorID,
        int categoryID,
        CancellationToken cancellationToken = default)
    {
        var result =
            await GetDonorLandingPageAsync(
                EMAOrganizationID,
                donorID,
                categoryID,
                cancellationToken);

        return result.Url;
    }

    public async Task<RouteAccessResult> CheckDonorRouteAccessAsync(
        int EMAOrganizationID,
        int donorID,
        string path,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var normalizedPath =
                NormalizeInternalUrl(path);

            var escapedPath =
                Uri.EscapeDataString(
                    normalizedPath);

            var result =
                await GetFromJsonWithRetryAsync<RouteAccessResult>(
                    $"{ApiBase}/organization/{EMAOrganizationID}/donors/{donorID}/route-access?path={escapedPath}",
                    cancellationToken);

            return result ?? new RouteAccessResult
            {
                IsAllowed = false,
                Reason =
                    "No donor route access response was returned."
            };
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new RouteAccessResult
            {
                IsAllowed = false,
                Reason =
                    "Donor route access could not be checked."
            };
        }
    }

    private async Task<T?> GetFromJsonWithRetryAsync<T>(
        string requestUri,
        CancellationToken cancellationToken)
    {
        Exception? finalException = null;

        for (var attempt = 1;
             attempt <= MaxAttempts;
             attempt++)
        {
            try
            {
                return await _http
                    .GetFromJsonAsync<T>(
                        requestUri,
                        cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                finalException = exception;

                if (attempt == MaxAttempts)
                {
                    break;
                }

                await Task.Delay(
                    RetryDelay,
                    cancellationToken);
            }
        }

        throw finalException ??
              new HttpRequestException(
                  "The request could not be completed.");
    }

    private static CategoryLandingPageResult CreateUnavailableResult(
        int categoryID)
    {
        return new CategoryLandingPageResult
        {
            CategoryID = categoryID,
            Url = string.Empty,
            IsFallback = true
        };
    }

    private static string NormalizeInternalUrl(
        string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "/access-denied";
        }

        var trimmedUrl =
            url.Trim();

        if (trimmedUrl.StartsWith(
                "http://",
                StringComparison.OrdinalIgnoreCase) ||
            trimmedUrl.StartsWith(
                "https://",
                StringComparison.OrdinalIgnoreCase))
        {
            return trimmedUrl;
        }

        var queryIndex =
            trimmedUrl.IndexOf(
                '?',
                StringComparison.Ordinal);

        if (queryIndex >= 0)
        {
            trimmedUrl =
                trimmedUrl[..queryIndex];
        }

        if (!trimmedUrl.StartsWith(
                "/",
                StringComparison.Ordinal))
        {
            trimmedUrl =
                $"/{trimmedUrl}";
        }

        return trimmedUrl.Length > 1
            ? trimmedUrl.TrimEnd('/')
            : trimmedUrl;
    }
}
