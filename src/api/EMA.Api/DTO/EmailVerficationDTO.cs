using EMA.Api.Services.Authentication;

namespace EMA.Api.DTOs;

public sealed class StartEmailVerificationRequestDto
{
    public TwoFactorPurpose Purpose { get; set; }

    public int ActorID { get; set; }

    public string EmailAddress { get; set; } =
        string.Empty;
}

public sealed class StartEmailVerificationResponseDto
{
    public string VerificationID { get; set; } =
        string.Empty;

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public string Message { get; set; } =
        string.Empty;
}

public sealed class VerifyEmailAddressRequestDto
{
    public TwoFactorPurpose Purpose { get; set; }

    public int ActorID { get; set; }

    public string EmailAddress { get; set; } =
        string.Empty;

    public string VerificationID { get; set; } =
        string.Empty;

    public string Code { get; set; } =
        string.Empty;
}

public sealed class VerifyEmailAddressResponseDto
{
    public string VerificationToken { get; set; } =
        string.Empty;

    public string Message { get; set; } =
        string.Empty;
}