namespace EMA.Api.Services.Security;

/*
 * Pure authorization decisions for the donation write endpoints.
 *
 * CreateDonation and UpdateDonation must never take the organization or
 * the acting user from the route or the request body. Keeping the
 * decision here - rather than inline in the functions - means it can be
 * tested without a SQL connection or an HttpRequestData harness, which
 * is the same split BlobGet and TenantBlobReadAccessPolicy already use.
 */
public static class DonationWriteAccessPolicy
{
    /// <summary>
    /// The organization a donation write must be performed against.
    /// Always the authenticated organization, never the route value.
    /// </summary>
    public static int ResolveOrganizationID(
        ApiRequestIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(
            identity);

        return identity.EMAOrganizationID;
    }

    /// <summary>
    /// The user recorded in CreatedbyUserID / UpdatedbyUserID. Always the
    /// authenticated user, so a client-supplied value cannot override it.
    /// </summary>
    public static int ResolveActingUserID(
        ApiRequestIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(
            identity);

        return identity.UserID;
    }

    /// <summary>
    /// A route organization that disagrees with the token is rejected
    /// rather than silently coerced, so a caller cannot probe for the
    /// existence of another tenant's data.
    /// </summary>
    public static bool IsRouteOrganizationAllowed(
        ApiRequestIdentity identity,
        int routeOrganizationID)
    {
        ArgumentNullException.ThrowIfNull(
            identity);

        return routeOrganizationID ==
               identity.EMAOrganizationID;
    }
}
