namespace EMA.Api.DTOs;

public sealed class SecureUnifiedLoginLookupRequestDto
{
    public string Identifier { get; set; } =
        string.Empty;
}

public sealed class SecureLoginVerificationStartResponseDto
{
    public bool RequiresVerification { get; set; } =
        true;

    public List<string> VerificationIDs { get; set; } =
        new();

    public string Message { get; set; } =
        string.Empty;

    public SecureUnifiedLoginLookupResponseDto?
        TrustedLogin { get; set; }
}

public sealed class SecureVerifyUnifiedLoginCodeRequestDto
{
    public string Identifier { get; set; } =
        string.Empty;

    public List<string> VerificationIDs { get; set; } =
        new();

    public string Code { get; set; } =
        string.Empty;
}

public sealed class SecureUnifiedLoginLookupResponseDto
{
    public string VerificationID { get; set; } =
        string.Empty;

    public bool IsTrustedDeviceLogin { get; set; }

    public List<UnifiedOrganizationUserChoiceDto> Users { get; set; } =
        new();

    public List<UnifiedDonorChoiceDto> Donors { get; set; } =
        new();

    public bool HasUserMatches =>
        Users.Count > 0;

    public bool HasDonorMatches =>
        Donors.Count > 0;
}

public sealed class SecureCompleteUnifiedLoginRequestDto
{
    public string Identifier { get; set; } =
        string.Empty;

    public string VerificationID { get; set; } =
        string.Empty;

    public bool UseTrustedDevice { get; set; }

    public bool TrustThisDevice { get; set; }

    public string ActorType { get; set; } =
        string.Empty;

    public int EMAOrganizationID { get; set; }

    public int? UserID { get; set; }

    public int? DonorID { get; set; }
}
