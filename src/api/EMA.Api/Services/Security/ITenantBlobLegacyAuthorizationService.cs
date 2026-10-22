namespace EMA.Api.Services.Security;

public interface
    ITenantBlobLegacyAuthorizationService
{
    Task<TenantBlobReadAuthorization>
        AuthorizeReadAsync(
            ApiRequestIdentity identity,
            string blobID,
            CancellationToken cancellationToken = default);

    Task<bool> IsReferencedByOrganizationAsync(
        int EMAOrganizationID,
        string blobID,
        CancellationToken cancellationToken = default);
}

public enum TenantBlobOrganizationReferenceKind
{
    None = 0,
    Logo = 1,
    SensitiveOrganizationData = 2
}

public sealed record TenantBlobReadAuthorization(
    bool IsAuthorized,
    bool AllowLegacyFallback,
    bool AllowSharedMenuItemMedia)
{
    public static TenantBlobReadAuthorization Denied { get; } =
        new(false, false, false);

    public static TenantBlobReadAuthorization TenantOnly { get; } =
        new(true, false, false);

    public static TenantBlobReadAuthorization TenantAndLegacy { get; } =
        new(true, true, false);

    public static TenantBlobReadAuthorization TenantAndSharedMenuItemMedia { get; } =
        new(true, false, true);
}

public static class TenantBlobReadAccessPolicy
{
    public static TenantBlobReadAuthorization Evaluate(
        ApiActorType actorType,
        TenantBlobOrganizationReferenceKind organizationReference,
        bool canAccessMenuItemMedia)
    {
        // A sensitive organization file must never become donor-readable merely
        // because the same opaque blob ID is also present on another record.
        if (organizationReference ==
            TenantBlobOrganizationReferenceKind.SensitiveOrganizationData)
        {
            return actorType == ApiActorType.OrganizationUser
                ? TenantBlobReadAuthorization.TenantAndLegacy
                : TenantBlobReadAuthorization.Denied;
        }

        // The organization logo is intentionally organization-visible branding.
        // It may still be read from the legacy namespace for older deployments.
        if (organizationReference ==
            TenantBlobOrganizationReferenceKind.Logo)
        {
            return TenantBlobReadAuthorization.TenantAndLegacy;
        }

        // MenuItem media first uses the current organization's tenant namespace.
        // Task 36 production recordings may also use the dedicated server-owned
        // shared MenuItem media namespace. The raw legacy/global namespace is
        // never used for MenuItem media.
        if (canAccessMenuItemMedia)
        {
            return TenantBlobReadAuthorization.TenantAndSharedMenuItemMedia;
        }

        return TenantBlobReadAuthorization.Denied;
    }
}
