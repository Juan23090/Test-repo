namespace EMA.Api.DTOs;

/// <summary>
/// Response used by the development login-options endpoint.
/// </summary>
public sealed class LoginOptionsResponseDto
{
    public List<LoginOrganizationOptionDto> Organizations { get; set; } = new();
}

public sealed class LoginOrganizationOptionDto
{
    public int EMAOrganizationID { get; set; }

    public string OrganizationName { get; set; } = string.Empty;

    public List<LoginUserOptionDto> Users { get; set; } = new();
}

public sealed class LoginUserOptionDto
{
    public int UserID { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string EmailAddress { get; set; } = string.Empty;

    public List<LoginRoleDto> Roles { get; set; } = new();
}

public sealed class LoginRoleDto
{
    public int RoleID { get; set; }

    public string RoleName { get; set; } = string.Empty;
}

public sealed class LoginLookupRequestDto
{
    public string Identifier { get; set; } = string.Empty;
}

public sealed class LoginLookupResponseDto
{
    public int UserID { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string EmailAddress { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    public string VerificationID { get; set; } = string.Empty;

    public string VerificationMessage { get; set; } = string.Empty;

    public List<LoginOrganizationChoiceDto> Organizations { get; set; } = new();
}

public sealed class LoginOrganizationChoiceDto
{
    public int EMAOrganizationID { get; set; }

    public string OrganizationName { get; set; } = string.Empty;
}

public sealed class DonorLoginLookupRequestDto
{
    public string Identifier { get; set; } = string.Empty;
}

public sealed class DonorLoginLookupResponseDto
{
    public string DonorName { get; set; } = string.Empty;

    public string EmailAddress { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    public string VerificationID { get; set; } = string.Empty;

    public string VerificationMessage { get; set; } = string.Empty;

    public List<DonorLoginOrganizationChoiceDto> Organizations { get; set; } = new();
}

public sealed class DonorLoginOrganizationChoiceDto
{
    public int EMAOrganizationID { get; set; }

    public string OrganizationName { get; set; } = string.Empty;

    public int DonorID { get; set; }
}

public sealed class UnifiedLoginLookupRequestDto
{
    public string Identifier { get; set; } = string.Empty;

    public string EmailAddress { get; set; } = string.Empty;
}

public sealed class UnifiedLoginLookupResponseDto
{
    public string PhoneNumber { get; set; } = string.Empty;

    public List<UnifiedOrganizationUserChoiceDto> Users { get; set; } = new();

    public List<UnifiedDonorChoiceDto> Donors { get; set; } = new();

    public bool HasUserMatches =>
        Users.Count > 0;

    public bool HasDonorMatches =>
        Donors.Count > 0;
}

public sealed class UnifiedOrganizationUserChoiceDto
{
    public int UserID { get; set; }

    public string UserName { get; set; } = string.Empty;

    public int EMAOrganizationID { get; set; }

    public string OrganizationName { get; set; } = string.Empty;
}

public sealed class UnifiedDonorChoiceDto
{
    public int DonorID { get; set; }

    public string DonorName { get; set; } = string.Empty;

    public int EMAOrganizationID { get; set; }

    public string OrganizationName { get; set; } = string.Empty;
}

public sealed class VerifyUnifiedLoginCodeRequestDto
{
    public string PhoneNumber { get; set; } = string.Empty;

    public string EmailAddress { get; set; } = string.Empty;

    public string VerificationID { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;
}

public sealed class CompleteUnifiedLoginRequestDto
{
    public string PhoneNumber { get; set; } = string.Empty;

    public string EmailAddress { get; set; } = string.Empty;

    public string VerificationID { get; set; } = string.Empty;

    public string ActorType { get; set; } = string.Empty;

    public int EMAOrganizationID { get; set; }

    public int? UserID { get; set; }

    public int? DonorID { get; set; }
}

public sealed class StartOrganizationLoginVerificationRequestDto
{
    public int EMAOrganizationID { get; set; }

    public int UserID { get; set; }
}

public sealed class StartDonorLoginVerificationRequestDto
{
    public int EMAOrganizationID { get; set; }

    public int DonorID { get; set; }
}

/// <summary>
/// Password remains optional until the old direct-login endpoints are removed.
/// </summary>
public sealed class LoginRequestDto
{
    public int EMAOrganizationID { get; set; }

    public int UserID { get; set; }

    public string? Password { get; set; }

    public string VerificationID { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;
}

public sealed class DonorLoginRequestDto
{
    public int EMAOrganizationID { get; set; }

    public int DonorID { get; set; }

    public string VerificationID { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;
}

public sealed class LoginResponseDto
{
    public int UserID { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string EmailAddress { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    public int EMAOrganizationID { get; set; }

    public string OrganizationName { get; set; } = string.Empty;

    public List<LoginRoleDto> Roles { get; set; } = new();

    public List<int> AllowedCategoryIDs { get; set; } = new();

    public int DefaultCategoryID { get; set; }

    public bool IsDonorSession { get; set; }

    public int? DonorID { get; set; }

    public string AccessToken { get; set; } =
        string.Empty;

    public DateTimeOffset?
        AccessTokenExpiresAtUtc
    {
        get;
        set;
    }
}

/// <summary>
/// Returned after a selected login path starts email verification.
/// The email address is intentionally not returned to the browser.
/// </summary>
public sealed class LoginVerificationStartResponseDto
{
    public string VerificationID { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}

public sealed class VerifyOrganizationLoginCodeRequestDto
{
    public int EMAOrganizationID { get; set; }

    public int UserID { get; set; }

    public string VerificationID { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;
}

public sealed class VerifyDonorLoginCodeRequestDto
{
    public int EMAOrganizationID { get; set; }

    public int DonorID { get; set; }

    public string VerificationID { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;
}
