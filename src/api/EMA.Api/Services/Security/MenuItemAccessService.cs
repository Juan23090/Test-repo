using Microsoft.Data.SqlClient;
using System.Data;

namespace EMA.Api.Services.Security;

/// <summary>
/// Backend enforcement of a screen's RoleMenuItem permission.
///
/// The endpoint policy registry decides *what kind of actor* may call an endpoint
/// (organization user, donor, administrator). It does not express "this caller holds the role
/// that grants a particular screen", so an endpoint backing a role-restricted screen is otherwise
/// callable by any authenticated user in the organization. The menu permission is a UI concern
/// only until something like this enforces it server-side.
///
/// Use this for endpoints that return sensitive data belonging to one screen — the tenant
/// boundary still applies on top, it is not a replacement for it.
/// </summary>
public interface IMenuItemAccessService
{
    /// <summary>
    /// True when the caller may use the screen served at <paramref name="menuItemUrl"/>.
    /// Resolution is by URL rather than MenuItemID because menu item IDs have been renumbered
    /// before; the route a page lives at is stable.
    /// </summary>
    Task<bool> CanAccessMenuItemUrlAsync(
        ApiRequestIdentity identity,
        string menuItemUrl,
        CancellationToken cancellationToken = default);
}

public sealed class SqlMenuItemAccessService : IMenuItemAccessService
{
    private readonly ApiRequestBoundaryValidator _boundaryValidator;
    private readonly string _connectionString;

    public SqlMenuItemAccessService(
        ApiRequestBoundaryValidator boundaryValidator)
    {
        _boundaryValidator = boundaryValidator;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    // Mirrors GetRouteAccess.UserCanAccessMenuItemAsync so the API agrees with the navigation
    // layer: administrators pass, everyone else needs a role that grants the item. Roles are
    // re-read from the database rather than taken from the token's claims, so a revoked role
    // takes effect immediately instead of when the session expires.
    public async Task<bool> CanAccessMenuItemUrlAsync(
        ApiRequestIdentity identity,
        string menuItemUrl,
        CancellationToken cancellationToken = default)
    {
        if (identity.ActorType != ApiActorType.OrganizationUser)
        {
            return false;
        }

        if (_boundaryValidator.IsOrganizationAdministrator(identity))
        {
            return true;
        }

        var normalizedUrl = NormalizeUrl(menuItemUrl);

        if (string.IsNullOrEmpty(normalizedUrl))
        {
            return false;
        }

        const string sql = """
            SELECT COUNT(1)
            FROM dbo.MenuItem mi
            INNER JOIN dbo.RoleMenuItem rmi
                ON mi.MenuItemID = rmi.MenuItemID
            INNER JOIN dbo.OrganizationUserRole ur
                ON rmi.RoleID = ur.RoleID
               AND ur.EMAOrganizationID = @OrgID
            INNER JOIN dbo.OrganizationUser ou
                ON ur.UserID = ou.UserID
            INNER JOIN dbo.[User] u
                ON ur.UserID = u.UserID
            INNER JOIN dbo.[Role] r
                ON ur.RoleID = r.RoleID
            WHERE ou.EMAOrganizationID = @OrgID
              AND ur.UserID = @UserID
              AND LOWER(LTRIM(RTRIM(mi.URL))) = @MenuItemUrl
              AND mi.IsActive = 'Y'
              AND u.IsActive = 1
              AND r.IsActive = 'Y';
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(sql, connection);

        command.Parameters.Add("@OrgID", SqlDbType.Int).Value = identity.EMAOrganizationID;
        command.Parameters.Add("@UserID", SqlDbType.Int).Value = identity.UserID;
        command.Parameters.Add("@MenuItemUrl", SqlDbType.VarChar, 500).Value = normalizedUrl;

        var count = await command.ExecuteScalarAsync(cancellationToken);

        return count is not null
            && count is not DBNull
            && Convert.ToInt32(count) > 0;
    }

    private static string NormalizeUrl(string? url)
    {
        var trimmed = url?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return string.Empty;
        }

        if (!trimmed.StartsWith('/'))
        {
            trimmed = "/" + trimmed;
        }

        return trimmed.ToLowerInvariant();
    }
}
