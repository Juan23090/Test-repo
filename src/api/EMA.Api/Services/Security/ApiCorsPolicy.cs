using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;

namespace EMA.Api.Services.Security;

public sealed class ApiCorsPolicy
{
    private static readonly string[]
        AllowedMethods =
        [
            HttpMethods.Get,
            HttpMethods.Post,
            HttpMethods.Put,
            HttpMethods.Delete,
            HttpMethods.Options
        ];

    private static readonly string[]
        AllowedHeaders =
        [
            "Authorization",
            "Content-Type",
            "Accept",
            "X-Sandbox-Capability",
            "X-EMA-Access-Token"
        ];

    private readonly HashSet<string>
        _allowedOrigins;

    private readonly bool _isProduction;

    public ApiCorsPolicy(
        IConfiguration configuration)
    {
        _isProduction =
            ApiRuntimeEnvironment
                .IsProductionLike;

        _allowedOrigins =
            (configuration["API_ALLOWED_ORIGINS"]
             ?? string.Empty)
                .Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries)
                .Select(
                    NormalizeOrigin)
                .Where(
                    origin =>
                        !string.IsNullOrWhiteSpace(
                            origin))
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);

        if (_isProduction &&
            _allowedOrigins.Count == 0)
        {
            throw new InvalidOperationException(
                "API_ALLOWED_ORIGINS must list the production frontend origins.");
        }
    }

    public bool IsRequestOriginAllowed(
        HttpRequest request)
    {
        var origin =
            request.Headers.Origin
                .ToString();

        if (string.IsNullOrWhiteSpace(
                origin))
        {
            return true;
        }

        var normalizedOrigin =
            NormalizeOrigin(
                origin);

        if (_allowedOrigins.Contains(
                normalizedOrigin))
        {
            return true;
        }

        return !_isProduction &&
               Uri.TryCreate(
                   normalizedOrigin,
                   UriKind.Absolute,
                   out var uri) &&
               uri.IsLoopback;
    }

    public bool IsValidPreflight(
        HttpRequest request)
    {
        if (!HttpMethods.IsOptions(
                request.Method) ||
            string.IsNullOrWhiteSpace(
                request.Headers.Origin))
        {
            return false;
        }

        var requestedMethod =
            request.Headers[
                    "Access-Control-Request-Method"]
                .ToString();

        return AllowedMethods.Contains(
            requestedMethod,
            StringComparer.OrdinalIgnoreCase);
    }

    public void ApplyResponseHeaders(
        HttpRequest request,
        HttpResponse response)
    {
        var origin =
            request.Headers.Origin
                .ToString();

        if (!string.IsNullOrWhiteSpace(
                origin) &&
            IsRequestOriginAllowed(
                request))
        {
            response.Headers[
                "Access-Control-Allow-Origin"] =
                NormalizeOrigin(
                    origin);

            response.Headers[
                "Access-Control-Allow-Credentials"] =
                "true";

            AppendVaryOrigin(
                response.Headers);
        }

        response.Headers[
            "Access-Control-Allow-Methods"] =
            string.Join(
                ", ",
                AllowedMethods);

        response.Headers[
            "Access-Control-Allow-Headers"] =
            string.Join(
                ", ",
                AllowedHeaders);

        response.Headers[
            "Access-Control-Max-Age"] =
            "600";

        response.Headers[
            "X-Content-Type-Options"] =
            "nosniff";

        response.Headers[
            "Referrer-Policy"] =
            "no-referrer";

        response.Headers[
            "Access-Control-Expose-Headers"] =
            "Content-Disposition";
    }

    public void ApplyResponseHeaders(
        HttpRequest request,
        HttpResponseData response)
    {
        var origin =
            request.Headers.Origin
                .ToString();

        if (!string.IsNullOrWhiteSpace(
                origin) &&
            IsRequestOriginAllowed(
                request))
        {
            SetHeader(
                response.Headers,
                "Access-Control-Allow-Origin",
                NormalizeOrigin(
                    origin));

            SetHeader(
                response.Headers,
                "Access-Control-Allow-Credentials",
                "true");

            AppendVaryOrigin(
                response.Headers);
        }

        SetHeader(
            response.Headers,
            "Access-Control-Allow-Methods",
            string.Join(
                ", ",
                AllowedMethods));

        SetHeader(
            response.Headers,
            "Access-Control-Allow-Headers",
            string.Join(
                ", ",
                AllowedHeaders));

        SetHeader(
            response.Headers,
            "Access-Control-Max-Age",
            "600");

        SetHeader(
            response.Headers,
            "Access-Control-Expose-Headers",
            "Content-Disposition");

        SetHeader(
            response.Headers,
            "X-Content-Type-Options",
            "nosniff");

        SetHeader(
            response.Headers,
            "Referrer-Policy",
            "no-referrer");
    }

    private static void AppendVaryOrigin(
        IHeaderDictionary headers)
    {
        var varyValues =
            headers.Vary
                .ToString()
                .Split(
                    ',',
                    StringSplitOptions
                        .RemoveEmptyEntries |
                    StringSplitOptions
                        .TrimEntries);

        if (varyValues.Contains(
                "Origin",
                StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        headers.Append(
            "Vary",
            "Origin");
    }

    private static void AppendVaryOrigin(
        HttpHeadersCollection headers)
    {
        if (headers.TryGetValues(
                "Vary",
                out var values) &&
            values
                .SelectMany(
                    value =>
                        value.Split(
                            ',',
                            StringSplitOptions
                                .RemoveEmptyEntries |
                            StringSplitOptions
                                .TrimEntries))
                .Contains(
                    "Origin",
                    StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        headers.Add(
            "Vary",
            "Origin");
    }

    private static void SetHeader(
        HttpHeadersCollection headers,
        string name,
        string value)
    {
        headers.Remove(
            name);

        headers.Add(
            name,
            value);
    }

    private static string NormalizeOrigin(
        string origin)
    {
        return origin
            .Trim()
            .TrimEnd('/');
    }
}