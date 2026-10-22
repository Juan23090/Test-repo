using Microsoft.Extensions.Logging;

namespace EMA.Api.Services.Authentication;

public sealed class DevelopmentTwoFactorEmailSender :
    ITwoFactorEmailSender
{
    private readonly ILogger<DevelopmentTwoFactorEmailSender> _logger;

    public DevelopmentTwoFactorEmailSender(
        ILogger<DevelopmentTwoFactorEmailSender> logger)
    {
        _logger =
            logger;
    }

    public Task SendCodeAsync(
        string emailAddress,
        string code,
        VerificationEmailKind emailKind,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "DEV verification code for {EmailAddress}. Kind: {EmailKind}; Code: {Code}",
            emailAddress,
            emailKind,
            code);

        return Task.CompletedTask;
    }

    public Task SendDonorRegistrationCodeAsync(
        string emailAddress,
        string code,
        string phoneNumber,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "DEV donor registration verification code for {EmailAddress}, phone {PhoneNumber}: {Code}",
            emailAddress,
            phoneNumber,
            code);

        return Task.CompletedTask;
    }
}