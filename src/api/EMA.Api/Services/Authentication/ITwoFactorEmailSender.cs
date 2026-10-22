namespace EMA.Api.Services.Authentication;

public interface ITwoFactorEmailSender
{
    Task SendCodeAsync(
        string emailAddress,
        string code,
        VerificationEmailKind emailKind,
        CancellationToken cancellationToken = default);

    Task SendDonorRegistrationCodeAsync(
        string emailAddress,
        string code,
        string phoneNumber,
        CancellationToken cancellationToken = default);
}