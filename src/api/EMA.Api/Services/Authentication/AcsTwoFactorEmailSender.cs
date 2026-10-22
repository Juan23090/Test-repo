using System.Data;
using EMA.Api.Services.Email;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Services.Authentication;

public sealed class AcsTwoFactorEmailSender :
    ITwoFactorEmailSender
{
    private readonly IEmailService _emailService;
    private readonly IEmailTemplateService _emailTemplateService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AcsTwoFactorEmailSender> _logger;
    private readonly string _connectionString;

    public AcsTwoFactorEmailSender(
        IEmailService emailService,
        IEmailTemplateService emailTemplateService,
        IConfiguration configuration,
        ILogger<AcsTwoFactorEmailSender> logger)
    {
        _emailService =
            emailService;

        _emailTemplateService =
            emailTemplateService;

        _configuration =
            configuration;

        _logger =
            logger;

        _connectionString =
            Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString.");
    }

    public async Task SendCodeAsync(
        string emailAddress,
        string code,
        VerificationEmailKind emailKind,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            emailAddress);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            code);

        var content =
            GetContent(
                emailKind);

        var htmlBody =
            await _emailTemplateService
                .RenderTemplateAsync(
                    content.TemplateFileName,
                    new Dictionary<string, string?>
                    {
                        ["Code"] =
                            code
                    },
                    cancellationToken);

        var plainTextBody =
            $"""
            {content.PlainTextIntroduction} {code}.

            This code will expire in 5 minutes.

            If you did not request this code, you can ignore this email.
            """;

        await SendHtmlEmailAsync(
            recipientAddress:
                emailAddress,

            subject:
                content.Subject,

            htmlBody:
                htmlBody,

            plainTextBody:
                plainTextBody,

            templateFileName:
                content.TemplateFileName,

            logMessage:
                "Verification email queued through ACS. OperationID: {OperationID}, Recipient: {Recipient}",

            cancellationToken:
                cancellationToken);
    }

    public Task SendLoginCodeAsync(
        string emailAddress,
        string code,
        CancellationToken cancellationToken = default) =>
        SendCodeAsync(
            emailAddress,
            code,
            VerificationEmailKind.Login,
            cancellationToken);

    public async Task SendDonorRegistrationCodeAsync(
        string emailAddress,
        string code,
        string phoneNumber,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            emailAddress);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            code);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            phoneNumber);

        const string subject =
            "Verify your EMA donor registration";

        var htmlBody =
            await _emailTemplateService
                .RenderTemplateAsync(
                    "DonorRegistrationVerification.html",
                    new Dictionary<string, string?>
                    {
                        ["Code"] =
                            code,

                        ["PhoneNumber"] =
                            phoneNumber
                    },
                    cancellationToken);

        var plainTextBody =
            $"""
            Please confirm that this phone number is correct: {phoneNumber}

            Your EMA donor registration verification code is {code}.

            This code will expire in 5 minutes.

            If you did not request this donor registration, you can ignore this email.
            """;

        await SendHtmlEmailAsync(
            recipientAddress:
                emailAddress,

            subject:
                subject,

            htmlBody:
                htmlBody,

            plainTextBody:
                plainTextBody,

            templateFileName:
                "DonorRegistrationVerification.html",

            logMessage:
                "Donor registration email queued through ACS. OperationID: {OperationID}, Recipient: {Recipient}",

            cancellationToken:
                cancellationToken);
    }

    private async Task SendHtmlEmailAsync(
        string recipientAddress,
        string subject,
        string htmlBody,
        string plainTextBody,
        string templateFileName,
        string logMessage,
        CancellationToken cancellationToken)
    {
        var senderAddress =
            _configuration["ACS_EMAIL_SENDER"];

        if (string.IsNullOrWhiteSpace(
                senderAddress))
        {
            throw new InvalidOperationException(
                "Missing ACS_EMAIL_SENDER setting.");
        }

        try
        {
            var operationID =
                await _emailService
                    .SendHtmlEmailAsync(
                        senderAddress:
                            senderAddress,

                        recipientAddress:
                            recipientAddress,

                        subject:
                            subject,

                        htmlBody:
                            htmlBody,

                        plainTextBody:
                            plainTextBody,

                        cancellationToken:
                            cancellationToken);

            await TryLogEmailAsync(
                recipientAddress,
                templateFileName,
                subject,
                operationID,
                "Queued",
                errorMessage:
                    null,
                cancellationToken);

            _logger.LogInformation(
                logMessage,
                operationID,
                recipientAddress);
        }
        catch (Exception exception)
        {
            await TryLogEmailAsync(
                recipientAddress,
                templateFileName,
                subject,
                operationID:
                    null,
                "Failed",
                exception.Message,
                cancellationToken);

            throw;
        }
    }

    private async Task TryLogEmailAsync(
        string recipientAddress,
        string templateFileName,
        string subject,
        string? operationID,
        string status,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO dbo.EmailSendLog
            (
                EMADonationID,
                DonationStepID,
                RecipientType,
                RecipientEmail,
                TemplateFileName,
                Subject,
                ProviderOperationID,
                Status,
                ErrorMessage
            )
            VALUES
            (
                NULL,
                NULL,
                @RecipientType,
                @RecipientEmail,
                @TemplateFileName,
                @Subject,
                @ProviderOperationID,
                @Status,
                @ErrorMessage
            );
            """;

        try
        {
            await using var connection =
                new SqlConnection(
                    _connectionString);

            await connection.OpenAsync(
                cancellationToken);

            await using var command =
                new SqlCommand(
                    sql,
                    connection);

            command.Parameters.Add(
                "@RecipientType",
                SqlDbType.VarChar,
                30)
                .Value =
                    "Authentication";

            command.Parameters.Add(
                "@RecipientEmail",
                SqlDbType.VarChar,
                255)
                .Value =
                    recipientAddress;

            command.Parameters.Add(
                "@TemplateFileName",
                SqlDbType.VarChar,
                255)
                .Value =
                    templateFileName;

            command.Parameters.Add(
                "@Subject",
                SqlDbType.NVarChar,
                1000)
                .Value =
                    subject;

            command.Parameters.Add(
                "@ProviderOperationID",
                SqlDbType.NVarChar,
                510)
                .Value =
                    string.IsNullOrWhiteSpace(
                        operationID)
                        ? DBNull.Value
                        : operationID;

            command.Parameters.Add(
                "@Status",
                SqlDbType.VarChar,
                30)
                .Value =
                    status;

            command.Parameters.Add(
                "@ErrorMessage",
                SqlDbType.NVarChar,
                -1)
                .Value =
                    string.IsNullOrWhiteSpace(
                        errorMessage)
                        ? DBNull.Value
                        : errorMessage;

            await command.ExecuteNonQueryAsync(
                cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Unable to write authentication email status to EmailSendLog. Recipient: {Recipient}, Status: {Status}",
                recipientAddress,
                status);
        }
    }

    private static VerificationEmailContent GetContent(
        VerificationEmailKind emailKind) =>
        emailKind switch
        {
            VerificationEmailKind.Login =>
                new VerificationEmailContent(
                    Subject:
                        "Your EMA login code",

                    TemplateFileName:
                        "LoginVerification.html",

                    PlainTextIntroduction:
                        "Your EMA login code is"),

            VerificationEmailKind.EmailAddressVerification =>
                new VerificationEmailContent(
                    Subject:
                        "Verify your email address for EMA",

                    TemplateFileName:
                        "EmailAddressVerification.html",

                    PlainTextIntroduction:
                        "Your EMA email verification code is"),

            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(emailKind),
                    emailKind,
                    "Unsupported verification email kind.")
        };

    private sealed record VerificationEmailContent(
        string Subject,
        string TemplateFileName,
        string PlainTextIntroduction);
}
