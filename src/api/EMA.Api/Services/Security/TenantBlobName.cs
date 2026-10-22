namespace EMA.Api.Services.Security;

public static class TenantBlobName
{
    public static string ForOrganization(
        int EMAOrganizationID,
        string blobID)
    {
        if (EMAOrganizationID <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(EMAOrganizationID));
        }

        if (string.IsNullOrWhiteSpace(
                blobID))
        {
            throw new ArgumentException(
                "BlobID is required.",
                nameof(blobID));
        }

        return $"org-{EMAOrganizationID}-{blobID.Trim()}";
    }
}
