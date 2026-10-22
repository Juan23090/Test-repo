using System.Net;
using Microsoft.AspNetCore.Http;

namespace EMA.Api.Services.Sandbox;

public static class ClientIPAddressResolver
{
    private static readonly string[] ForwardedHeaderNames =
    [
        "X-Azure-ClientIP",
        "CF-Connecting-IP",
        "X-Forwarded-For",
        "X-Real-IP"
    ];

    public static string GetClientIPAddress(
        HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(
            request);

        var remoteAddress =
            NormalizeIPAddress(
                request
                    .HttpContext
                    .Connection
                    .RemoteIpAddress);

        if (IsTrustedProxy(
                remoteAddress))
        {
            foreach (var headerName in
                     ForwardedHeaderNames)
            {
                if (!request.Headers.TryGetValue(
                        headerName,
                        out var values))
                {
                    continue;
                }

                var firstValue =
                    values
                        .FirstOrDefault()?
                        .Split(
                            ',',
                            StringSplitOptions
                                .RemoveEmptyEntries |
                            StringSplitOptions
                                .TrimEntries)
                        .FirstOrDefault();

                var normalized =
                    NormalizeIPAddress(
                        firstValue);

                if (!string.IsNullOrWhiteSpace(
                        normalized))
                {
                    return normalized;
                }
            }
        }

        return remoteAddress
               ?? string.Empty;
    }

    private static string? NormalizeIPAddress(
        object? value)
    {
        if (value is null)
        {
            return null;
        }

        var text =
            value switch
            {
                IPAddress address =>
                    address.ToString(),

                string stringValue =>
                    stringValue.Trim(),

                _ =>
                    value.ToString()?.Trim()
                    ?? string.Empty
            };

        if (text.Length == 0)
        {
            return null;
        }

        if (text.StartsWith(
                "[",
                StringComparison.Ordinal) &&
            text.Contains(
                "]",
                StringComparison.Ordinal))
        {
            text =
                text[
                    1..text.IndexOf(
                        ']')];
        }

        if (IPAddress.TryParse(
                text,
                out var parsedAddress))
        {
            if (parsedAddress.IsIPv4MappedToIPv6)
            {
                parsedAddress =
                    parsedAddress.MapToIPv4();
            }

            return Truncate(
                parsedAddress.ToString(),
                50);
        }

        var lastColon =
            text.LastIndexOf(
                ':');

        if (lastColon > 0 &&
            text.Count(character =>
                character == ':') == 1)
        {
            var possibleIPv4 =
                text[..lastColon];

            if (IPAddress.TryParse(
                    possibleIPv4,
                    out parsedAddress))
            {
                return Truncate(
                    parsedAddress.ToString(),
                    50);
            }
        }

        return null;
    }

    private static bool IsTrustedProxy(
        string? remoteAddress)
    {
        if (string.IsNullOrWhiteSpace(
                remoteAddress))
        {
            return false;
        }

        var trustedProxyAddresses =
            Environment.GetEnvironmentVariable(
                "TRUSTED_PROXY_IPS");

        if (string.IsNullOrWhiteSpace(
                trustedProxyAddresses))
        {
            return false;
        }

        return trustedProxyAddresses
            .Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Select(
                NormalizeIPAddress)
            .Any(
                trustedAddress =>
                    string.Equals(
                        trustedAddress,
                        remoteAddress,
                        StringComparison.OrdinalIgnoreCase));
    }

    private static string Truncate(
        string value,
        int maxLength)
    {
        return value.Length <= maxLength
            ? value
            : value[..maxLength];
    }
}