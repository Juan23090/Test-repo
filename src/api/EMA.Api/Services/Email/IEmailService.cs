namespace EMA.Api.Services.Email;

public interface IEmailService
{
    Task<string> SendHtmlEmailAsync(
        string senderAddress,
        string recipientAddress,
        string subject,
        string htmlBody,
        string? plainTextBody = null,
        CancellationToken cancellationToken = default);

    Task<string> SendHtmlEmailAsync(
        string senderAddress,
        IEnumerable<string> recipientAddresses,
        string subject,
        string htmlBody,
        string? plainTextBody = null,
        CancellationToken cancellationToken = default);

    Task<string> SendHtmlEmailWithAttachmentAsync(
        string senderAddress,
        string recipientAddress,
        string subject,
        string htmlBody,
        string attachmentName,
        byte[] attachmentBytes,
        string? plainTextBody = null,
        CancellationToken cancellationToken = default);
}