namespace EMA.App.Models;

public sealed class LoginLookupRequest
{
    public string Identifier { get; set; } = string.Empty;
}

public sealed class LoginLookupResponse
{
    public int UserID { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string EmailAddress { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    public string VerificationID { get; set; } = string.Empty;

    public string VerificationMessage { get; set; } = string.Empty;

    public List<LoginOrganizationChoice> Organizations { get; set; } = new();
}

public sealed class LoginOrganizationChoice
{
    public int EMAOrganizationID { get; set; }

    public string OrganizationName { get; set; } = string.Empty;
}

public sealed class DonorLoginLookupRequest
{
    public string Identifier { get; set; } = string.Empty;
}

public sealed class DonorLoginLookupResponse
{
    public string DonorName { get; set; } = string.Empty;

    public string EmailAddress { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    public string VerificationID { get; set; } = string.Empty;

    public string VerificationMessage { get; set; } = string.Empty;

    public List<DonorLoginOrganizationChoice> Organizations { get; set; } = new();
}

public sealed class DonorLoginOrganizationChoice
{
    public int EMAOrganizationID { get; set; }

    public string OrganizationName { get; set; } = string.Empty;

    public int DonorID { get; set; }
}

public sealed class UnifiedLoginLookupRequest
{
    public string Identifier { get; set; } = string.Empty;

    public string EmailAddress { get; set; } = string.Empty;
}

public sealed class UnifiedLoginLookupResponse
{
    public string PhoneNumber { get; set; } = string.Empty;

    public List<UnifiedOrganizationUserChoice> Users { get; set; } = new();

    public List<UnifiedDonorChoice> Donors { get; set; } = new();

    public bool HasUserMatches =>
        Users.Count > 0;

    public bool HasDonorMatches =>
        Donors.Count > 0;
}

public sealed class UnifiedOrganizationUserChoice
{
    public int UserID { get; set; }

    public string UserName { get; set; } = string.Empty;

    public int EMAOrganizationID { get; set; }

    public string OrganizationName { get; set; } = string.Empty;
}

public sealed class UnifiedDonorChoice
{
    public int DonorID { get; set; }

    public string DonorName { get; set; } = string.Empty;

    public int EMAOrganizationID { get; set; }

    public string OrganizationName { get; set; } = string.Empty;
}

public sealed class VerifyUnifiedLoginCodeRequest
{
    public string PhoneNumber { get; set; } = string.Empty;

    public string EmailAddress { get; set; } = string.Empty;

    public string VerificationID { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;
}

public sealed class CompleteUnifiedLoginRequest
{
    public string PhoneNumber { get; set; } = string.Empty;

    public string EmailAddress { get; set; } = string.Empty;

    public string VerificationID { get; set; } = string.Empty;

    public string ActorType { get; set; } = string.Empty;

    public int EMAOrganizationID { get; set; }

    public int? UserID { get; set; }

    public int? DonorID { get; set; }
}

public sealed class StartOrganizationLoginVerificationRequest
{
    public int EMAOrganizationID { get; set; }

    public int UserID { get; set; }
}

public sealed class StartDonorLoginVerificationRequest
{
    public int EMAOrganizationID { get; set; }

    public int DonorID { get; set; }
}

public sealed class LoginVerificationStartResponse
{
    public string VerificationID { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}

public sealed class LoginRequest
{
    public int EMAOrganizationID { get; set; }

    public int UserID { get; set; }

    public string? Password { get; set; }

    public string VerificationID { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;
}

public sealed class DonorLoginRequest
{
    public int EMAOrganizationID { get; set; }

    public int DonorID { get; set; }

    public string VerificationID { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;
}

public sealed class VerifyOrganizationLoginCodeRequest
{
    public int EMAOrganizationID { get; set; }

    public int UserID { get; set; }

    public string VerificationID { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;
}

public sealed class VerifyDonorLoginCodeRequest
{
    public int EMAOrganizationID { get; set; }

    public int DonorID { get; set; }

    public string VerificationID { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;
}

public sealed class LoginResponse
{
    public int UserID { get; set; }

    public string UserName { get; set; } =
        string.Empty;

    public string EmailAddress { get; set; } =
        string.Empty;

    public string PhoneNumber { get; set; } =
        string.Empty;

    public int EMAOrganizationID { get; set; }

    public string OrganizationName { get; set; } =
        string.Empty;

    public List<LoginRole> Roles { get; set; } =
        new();

    public List<int> AllowedCategoryIDs { get; set; } =
        new();

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
public sealed class LoginRole
{
    public int RoleID { get; set; }

    public string RoleName { get; set; } = string.Empty;
}

public sealed class PublicOrganizationLookup
{
    public int EMAOrganizationID { get; set; }

    public string OrganizationName { get; set; } = string.Empty;
}

public sealed class DonorOrganizationLoginLookupRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
}

public sealed class DonorOrganizationLoginLookupResponse
{
    public int EMAOrganizationID { get; set; }

    public string OrganizationName { get; set; } = string.Empty;

    public int DonorID { get; set; }

    public string DonorName { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;
}

public sealed class StartDonorSelfRegistrationVerificationRequest
{
    public int EMAOrganizationID { get; set; }

    public string OfficialDonorName { get; set; } = string.Empty;

    public string? InteracDonorName { get; set; }

    public string Address { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    public string ProvinceStateCode { get; set; } = string.Empty;

    public string PostalCode { get; set; } = string.Empty;

    public string Country { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    public string EmailAddress { get; set; } = string.Empty;
}

public sealed class StartDonorSelfRegistrationVerificationResponse
{
    public string RegistrationID { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}

public sealed class VerifyDonorSelfRegistrationRequest
{
    public string RegistrationID { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;
}

public sealed class LoginApiError
{
    public string Message { get; set; } = string.Empty;
}

public sealed class LoginServiceResult<T>
{
    private LoginServiceResult(
        bool isSuccess,
        T? value,
        string? errorMessage)
    {
        IsSuccess =
            isSuccess;

        Value =
            value;

        ErrorMessage =
            errorMessage;
    }

    public bool IsSuccess { get; }

    public T? Value { get; }

    public string? ErrorMessage { get; }

    public static LoginServiceResult<T> Success(
        T value)
    {
        return new LoginServiceResult<T>(
            true,
            value,
            null);
    }

    public static LoginServiceResult<T> Failure(
        string errorMessage)
    {
        return new LoginServiceResult<T>(
            false,
            default,
            errorMessage);
    }
}
