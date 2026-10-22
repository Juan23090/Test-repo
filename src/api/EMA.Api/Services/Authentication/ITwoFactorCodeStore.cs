namespace EMA.Api.Services.Authentication;

public interface ITwoFactorCodeStore
{
    Task<TwoFactorCodeRecord> CreateCodeAsync(
        TwoFactorPurpose purpose,
        int actorID,
        string emailAddress,
        CancellationToken cancellationToken = default);

    Task<bool> VerifyCodeAsync(
        string verificationID,
        TwoFactorPurpose purpose,
        int actorID,
        string? emailAddress,
        string code,
        bool consumeOnSuccess = true,
        CancellationToken cancellationToken = default);

    Task<bool> ConsumeVerifiedCodeAsync(
        string verificationID,
        TwoFactorPurpose purpose,
        int actorID,
        string emailAddress,
        CancellationToken cancellationToken = default);

    Task RevokeCodeAsync(
        string verificationID,
        CancellationToken cancellationToken = default);
}