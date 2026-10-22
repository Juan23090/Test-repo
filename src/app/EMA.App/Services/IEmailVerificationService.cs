using EMA.App.Models;

namespace EMA.App.Services;

public interface IEmailVerificationService
{
    Task<EmailVerificationServiceResult<StartEmailVerificationResponse>>
        StartAsync(
            EmailVerificationPurpose purpose,
            int actorID,
            string emailAddress,
            CancellationToken cancellationToken = default);

    Task<EmailVerificationServiceResult<VerifyEmailAddressResponse>>
        VerifyAsync(
            EmailVerificationPurpose purpose,
            int actorID,
            string emailAddress,
            string verificationID,
            string code,
            CancellationToken cancellationToken = default);
}