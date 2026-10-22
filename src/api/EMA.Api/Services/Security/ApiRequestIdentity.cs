using System.Security.Claims;

namespace EMA.Api.Services.Security;

public sealed record ApiRequestIdentity(
    Guid SessionID,
    Guid SecurityStamp,
    ApiActorType ActorType,
    int EMAOrganizationID,
    int UserID,
    int? DonorID,
    IReadOnlyList<int> RoleIDs,
    IReadOnlyList<int> AllowedCategoryIDs,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc)
{
    public const string AuthenticationType =
        "EMA-AccessToken";

    public const string SessionIDClaim =
        "ema:sid";

    public const string SecurityStampClaim =
        "ema:stamp";

    public const string ActorTypeClaim =
        "ema:actor_type";

    public const string OrganizationIDClaim =
        "ema:organization_id";

    public const string UserIDClaim =
        "ema:user_id";

    public const string DonorIDClaim =
        "ema:donor_id";

    public const string RoleIDClaim =
        "ema:role_id";

    public const string CategoryIDClaim =
        "ema:category_id";

    public const string IssuedAtClaim =
        "ema:issued_at";

    public const string ExpiresAtClaim =
        "ema:expires_at";

    public int ActorID =>
        ActorType == ApiActorType.Donor
            ? DonorID ?? UserID
            : UserID;

    public ClaimsPrincipal ToClaimsPrincipal()
    {
        var claims =
            new List<Claim>
            {
                new(
                    ClaimTypes.NameIdentifier,
                    ActorID.ToString()),

                new(
                    SessionIDClaim,
                    SessionID.ToString("N")),

                new(
                    SecurityStampClaim,
                    SecurityStamp.ToString("N")),

                new(
                    ActorTypeClaim,
                    ActorType.ToString()),

                new(
                    OrganizationIDClaim,
                    EMAOrganizationID.ToString()),

                new(
                    UserIDClaim,
                    UserID.ToString()),

                new(
                    IssuedAtClaim,
                    IssuedAtUtc.ToUnixTimeSeconds().ToString()),

                new(
                    ExpiresAtClaim,
                    ExpiresAtUtc.ToUnixTimeSeconds().ToString())
            };

        if (DonorID.HasValue)
        {
            claims.Add(
                new Claim(
                    DonorIDClaim,
                    DonorID.Value.ToString()));
        }

        claims.AddRange(
            RoleIDs.Select(
                roleID =>
                    new Claim(
                        RoleIDClaim,
                        roleID.ToString())));

        claims.AddRange(
            AllowedCategoryIDs.Select(
                categoryID =>
                    new Claim(
                        CategoryIDClaim,
                        categoryID.ToString())));

        return new ClaimsPrincipal(
            new ClaimsIdentity(
                claims,
                AuthenticationType));
    }

    public static bool TryFromPrincipal(
        ClaimsPrincipal? principal,
        out ApiRequestIdentity? identity)
    {
        identity = null;

        if (principal?.Identity?.IsAuthenticated !=
            true ||
            !string.Equals(
                principal.Identity.AuthenticationType,
                AuthenticationType,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (!Guid.TryParseExact(
                principal.FindFirstValue(
                    SessionIDClaim),
                "N",
                out var sessionID) ||
            !Guid.TryParseExact(
                principal.FindFirstValue(
                    SecurityStampClaim),
                "N",
                out var securityStamp) ||
            !Enum.TryParse<ApiActorType>(
                principal.FindFirstValue(
                    ActorTypeClaim),
                ignoreCase: false,
                out var actorType) ||
            !Enum.IsDefined(
                actorType) ||
            !int.TryParse(
                principal.FindFirstValue(
                    OrganizationIDClaim),
                out var organizationID) ||
            !int.TryParse(
                principal.FindFirstValue(
                    UserIDClaim),
                out var userID) ||
            !long.TryParse(
                principal.FindFirstValue(
                    IssuedAtClaim),
                out var issuedAtUnixSeconds) ||
            !long.TryParse(
                principal.FindFirstValue(
                    ExpiresAtClaim),
                out var expiresAtUnixSeconds))
        {
            return false;
        }

        int? donorID = null;

        var donorIDValue =
            principal.FindFirstValue(
                DonorIDClaim);

        if (!string.IsNullOrWhiteSpace(
                donorIDValue))
        {
            if (!int.TryParse(
                    donorIDValue,
                    out var parsedDonorID))
            {
                return false;
            }

            donorID =
                parsedDonorID;
        }

        var roleIDs =
            ParseIntegerClaims(
                principal,
                RoleIDClaim);

        var categoryIDs =
            ParseIntegerClaims(
                principal,
                CategoryIDClaim);

        if (sessionID == Guid.Empty ||
            securityStamp == Guid.Empty ||
            organizationID <= 0 ||
            userID <= 0 ||
            roleIDs is null ||
            categoryIDs is null ||
            actorType == ApiActorType.Donor &&
            (!donorID.HasValue ||
             donorID.Value <= 0) ||
            actorType ==
                ApiActorType.OrganizationUser &&
            donorID.HasValue)
        {
            return false;
        }

        identity =
            new ApiRequestIdentity(
                sessionID,
                securityStamp,
                actorType,
                organizationID,
                userID,
                donorID,
                roleIDs,
                categoryIDs,
                DateTimeOffset.FromUnixTimeSeconds(
                    issuedAtUnixSeconds),
                DateTimeOffset.FromUnixTimeSeconds(
                    expiresAtUnixSeconds));

        return true;
    }

    private static List<int>?
        ParseIntegerClaims(
            ClaimsPrincipal principal,
            string claimType)
    {
        var values =
            new List<int>();

        foreach (var claim in principal.FindAll(
                     claimType))
        {
            if (!int.TryParse(
                    claim.Value,
                    out var value) ||
                value <= 0)
            {
                return null;
            }

            values.Add(
                value);
        }

        return values
            .Distinct()
            .OrderBy(value => value)
            .ToList();
    }
}
