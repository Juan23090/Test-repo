using System.Text;
using System.Xml.Linq;

namespace EMA.App.Models;

public static class MenuItemResourceSvg
{
    private static readonly HashSet<string> AllowedElements =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "svg",
            "path",
            "g",
            "circle",
            "rect",
            "line",
            "polyline",
            "polygon"
        };

    private static readonly HashSet<string> AllowedAttributes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "xmlns",
            "width",
            "height",
            "fill",
            "class",
            "viewBox",
            "d",
            "fill-rule",
            "clip-rule",
            "cx",
            "cy",
            "r",
            "x",
            "y",
            "x1",
            "x2",
            "y1",
            "y2",
            "points",
            "rx",
            "ry"
        };

    public static string? GetSafeDataUri(
        string? rawSvg)
    {
        if (!TrySanitize(
                rawSvg,
                out var sanitizedSvg))
        {
            return null;
        }

        return
            "data:image/svg+xml;base64," +
            Convert.ToBase64String(
                Encoding.UTF8.GetBytes(
                    sanitizedSvg));
    }

    private static bool TrySanitize(
        string? rawSvg,
        out string sanitizedSvg)
    {
        sanitizedSvg = string.Empty;

        if (string.IsNullOrWhiteSpace(rawSvg) ||
            rawSvg.Length > 20_000)
        {
            return false;
        }

        try
        {
            var document =
                XDocument.Parse(
                    rawSvg,
                    LoadOptions.None);

            var root = document.Root;

            if (root is null ||
                !root.Name.LocalName.Equals(
                    "svg",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            foreach (var element in root.DescendantsAndSelf())
            {
                if (!AllowedElements.Contains(
                        element.Name.LocalName))
                {
                    return false;
                }

                foreach (var attribute in element.Attributes())
                {
                    var localName =
                        attribute.IsNamespaceDeclaration
                            ? "xmlns"
                            : attribute.Name.LocalName;

                    if (!AllowedAttributes.Contains(localName) ||
                        localName.StartsWith(
                            "on",
                            StringComparison.OrdinalIgnoreCase) ||
                        attribute.Value.Contains(
                            "javascript:",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
            }

            sanitizedSvg =
                document.ToString(
                    SaveOptions.DisableFormatting);

            return true;
        }
        catch
        {
            return false;
        }
    }
}
