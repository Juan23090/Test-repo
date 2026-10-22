namespace EMA.Api.Services.Security;

public static class SharedMenuItemMediaBlobName
{
    private const string Prefix = "menu-media-";

    public static string ForBlobID(string blobID)
    {
        if (string.IsNullOrWhiteSpace(blobID))
        {
            throw new ArgumentException(
                "BlobID is required.",
                nameof(blobID));
        }

        var normalized = blobID.Trim();

        if (normalized.Length > 36 ||
            normalized.Any(char.IsControl) ||
            normalized.Contains('/') ||
            normalized.Contains('\\'))
        {
            throw new ArgumentException(
                "MenuItem media BlobID is invalid.",
                nameof(blobID));
        }

        return $"{Prefix}{normalized}";
    }
}
