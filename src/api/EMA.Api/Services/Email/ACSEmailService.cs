using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Services.Email;

public sealed class ACSEmailService : IEmailService
{
    private readonly EmailClient _emailClient;
    private readonly ILogger<ACSEmailService> _logger;

    public ACSEmailService(
        IConfiguration configuration,
        ILogger<ACSEmailService> logger)
    {
        _logger = logger;

        var connectionString =
            configuration["COMMUNICATION_SERVICES_CONNECTION_STRING"]
            ?? configuration["AzureCommunicationServices:ConnectionString"];

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ACS email connection string is missing. Set COMMUNICATION_SERVICES_CONNECTION_STRING.");
        }

        _emailClient = new EmailClient(connectionString);
    }

    public Task<string> SendHtmlEmailAsync(
        string senderAddress,
        string recipientAddress,
        string subject,
        string htmlBody,
        string? plainTextBody = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientAddress);

        return SendHtmlEmailAsync(
            senderAddress: senderAddress,
            recipientAddresses: new[] { recipientAddress },
            subject: subject,
            htmlBody: htmlBody,
            plainTextBody: plainTextBody,
            cancellationToken: cancellationToken);
    }

    public async Task<string> SendHtmlEmailAsync(
        string senderAddress,
        IEnumerable<string> recipientAddresses,
        string subject,
        string htmlBody,
        string? plainTextBody = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(senderAddress);
        ArgumentNullException.ThrowIfNull(recipientAddresses);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(htmlBody);

        var recipients = recipientAddresses
            .Where(email => !string.IsNullOrWhiteSpace(email))
            .Select(email => email.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (recipients.Count == 0)
        {
            throw new ArgumentException(
                "At least one recipient email address is required.",
                nameof(recipientAddresses));
        }

        var emailContent = new EmailContent(subject)
        {
            PlainText = plainTextBody ?? "This email requires an HTML-compatible email client.",
            Html = htmlBody
        };

        var emailRecipients = new EmailRecipients(
            recipients.Select(email => new EmailAddress(email)).ToList());

        var emailMessage = new EmailMessage(
            senderAddress: senderAddress,
            recipients: emailRecipients,
            content: emailContent);

        try
        {
            EmailSendOperation operation = await _emailClient.SendAsync(
                wait: WaitUntil.Started,
                message: emailMessage,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "ACS email queued. OperationId: {OperationId}, To: {Recipients}, Subject: {Subject}",
                operation.Id,
                string.Join(", ", recipients),
                subject);

            return operation.Id;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(
                ex,
                "ACS email failed. Status: {Status}, ErrorCode: {ErrorCode}, To: {Recipients}, Subject: {Subject}",
                ex.Status,
                ex.ErrorCode,
                string.Join(", ", recipients),
                subject);

            throw;
        }
    }

    public async Task<string> SendHtmlEmailWithAttachmentAsync(
        string senderAddress,
        string recipientAddress,
        string subject,
        string htmlBody,
        string attachmentName,
        byte[] attachmentBytes,
        string? plainTextBody = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(senderAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(htmlBody);
        ArgumentException.ThrowIfNullOrWhiteSpace(attachmentName);
        ArgumentNullException.ThrowIfNull(attachmentBytes);

        var emailContent = new EmailContent(subject)
        {
            PlainText = plainTextBody ?? "This email requires an HTML-compatible email client.",
            Html = htmlBody
        };

        var emailRecipients = new EmailRecipients(new[] { new EmailAddress(recipientAddress) });

        var emailMessage = new EmailMessage(
            senderAddress: senderAddress,
            recipients: emailRecipients,
            content: emailContent);

        var emailAttachment = new EmailAttachment(
            name: attachmentName,
            contentType: "application/pdf",
            content: new BinaryData(attachmentBytes)
        );
        emailMessage.Attachments.Add(emailAttachment);

        try
        {
            EmailSendOperation operation = await _emailClient.SendAsync(
                wait: WaitUntil.Started,
                message: emailMessage,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "ACS email with attachment queued. OperationId: {OperationId}, To: {Recipient}, Subject: {Subject}",
                operation.Id,
                recipientAddress,
                subject);

            return operation.Id;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(
                ex,
                "ACS email with attachment failed. Status: {Status}, ErrorCode: {ErrorCode}, To: {Recipient}, Subject: {Subject}",
                ex.Status,
                ex.ErrorCode,
                recipientAddress,
                subject);

            throw;
        }
    }
}