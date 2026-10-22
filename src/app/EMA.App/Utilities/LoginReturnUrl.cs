namespace EMA.App.Utilities;

public static class LoginReturnUrl
{
    public static bool TryGetSafeLocalUrl(
        string? candidate,
        out string localUrl)
    {
        localUrl =
            string.Empty;

        if (string.IsNullOrWhiteSpace(
                candidate))
        {
            return false;
        }

        var decoded =
            candidate.Trim();

        /*
         * Query-string binding may already decode the value once. Decode only
         * while an escaped local URL remains, and never more than twice.
         */
        for (var attempt = 0;
             attempt < 2 &&
             decoded.Contains(
                 '%',
                 StringComparison.Ordinal);
             attempt++)
        {
            try
            {
                var next =
                    Uri.UnescapeDataString(
                        decoded);

                if (next.Equals(
                        decoded,
                        StringComparison.Ordinal))
                {
                    break;
                }

                decoded =
                    next;
            }
            catch (UriFormatException)
            {
                return false;
            }
        }

        if (!decoded.StartsWith(
                "/",
                StringComparison.Ordinal)
            || decoded.StartsWith(
                "//",
                StringComparison.Ordinal)
            || decoded.Contains('\\')
            || decoded.Contains(
                '\r')
            || decoded.Contains(
                '\n'))
        {
            return false;
        }

        if (decoded.Equals(
                "/login",
                StringComparison.OrdinalIgnoreCase)
            || decoded.StartsWith(
                "/login?",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        localUrl =
            decoded;

        return true;
    }

    public static string BuildLoginUrl(
        string? localUrl)
    {
        return TryGetSafeLocalUrl(
            localUrl,
            out var safeUrl)
                ? $"/login?returnUrl={Uri.EscapeDataString(safeUrl)}"
                : "/login";
    }

    public static string BuildSessionExpiredLoginUrl(
        string? localUrl)
    {
        var loginUrl =
            BuildLoginUrl(
                localUrl);

        var separator =
            loginUrl.Contains(
                '?',
                StringComparison.Ordinal)
                ? '&'
                : '?';

        return
            $"{loginUrl}{separator}sessionExpired=true";
    }

    public static string AppendToUrl(
        string url,
        string? returnUrl)
    {
        if (!TryGetSafeLocalUrl(
                returnUrl,
                out var safeUrl))
        {
            return url;
        }

        var separator =
            url.Contains(
                '?',
                StringComparison.Ordinal)
                ? '&'
                : '?';

        return
            $"{url}{separator}returnUrl={Uri.EscapeDataString(safeUrl)}";
    }
}
