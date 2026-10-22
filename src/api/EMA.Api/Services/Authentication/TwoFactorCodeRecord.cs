namespace EMA.Api.Services.Authentication;

public sealed class TwoFactorCodeRecord
{
    public required string VerificationID { get; init; }

    public required string Code { get; init; }

    public required DateTimeOffset ExpiresAtUtc { get; init; }
}