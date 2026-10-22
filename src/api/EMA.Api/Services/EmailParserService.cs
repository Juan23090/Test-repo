using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using MimeKit;

namespace EMA.Api.Services;

public interface IEmailParserService
{
    ParsedDonationEmail Parse(
        byte[] rawMime,
        GraphEmailMessage graphMessage,
        string emailBlobID);
}

public sealed class EmailParserService : IEmailParserService
{
    public ParsedDonationEmail Parse(
        byte[] rawMime,
        GraphEmailMessage graphMessage,
        string emailBlobID)
    {
        if (rawMime is null || rawMime.Length == 0)
        {
            throw new ArgumentException("Raw MIME email content is required.", nameof(rawMime));
        }

        if (string.IsNullOrWhiteSpace(emailBlobID))
        {
            throw new ArgumentException("EmailBlobID is required.", nameof(emailBlobID));
        }

        using var stream = new MemoryStream(rawMime);
        var message = MimeMessage.Load(stream);

        var plainText = message.TextBody;

        if (string.IsNullOrWhiteSpace(plainText) && !string.IsNullOrWhiteSpace(message.HtmlBody))
        {
            plainText = HtmlToText(message.HtmlBody);
        }

        plainText ??= string.Empty;

        var donorName =
            ExtractLineValue(plainText, "Sent From")
            ?? GetMailboxName(message.From.Mailboxes.FirstOrDefault())
            ?? graphMessage.FromName
            ?? "Unknown Donor";

        var donorEmail =
            GetUsableDonorEmail(message.ReplyTo.Mailboxes.FirstOrDefault()?.Address)
            ?? GetUsableDonorEmail(graphMessage.ReplyToAddress)
            ?? GetUsableDonorEmail(
                ExtractLineValue(
                    plainText,
                    "Donor Email"));

        var amount = ExtractAmount(plainText);
        var referenceNumber = ExtractLineValue(plainText, "Reference Number");
        var donorMessage = ExtractLineValue(plainText, "Message");
        // Sandbox-generated MIME can include the logical sandbox recipient in the
        // body. Prefer it so organization resolution targets
        // Organization.SandboxEtransferEmail even if transport routing changes.
        var recipientEmail =
            GetUsableRecipientEmail(
                ExtractLineValue(
                    plainText,
                    "Sandbox Email"))
            ?? GetUsableRecipientEmail(
                message.To.Mailboxes.FirstOrDefault()?.Address)
            ?? GetUsableRecipientEmail(
                graphMessage.RecipientEmailAddress);

        var globalMessageID =
            graphMessage.InternetMessageId
            ?? NormalizeMessageID(message.MessageId);

        var receivedDate =
            graphMessage.ReceivedDateTime
            ?? message.Date;

        var financialInstitution = ExtractFinancialInstitution(plainText);
        var depositInstitution = ExtractDepositInstitution(plainText);

        var paymentKey =
            NullIfWhiteSpace(message.Headers["X-PaymentKey"])
            ?? referenceNumber;

        var paymentNotificationID =
            NullIfWhiteSpace(message.Headers["X-Payment-Notification"]);

        var givingNumber = ExtractGivingNumber(plainText);

        return new ParsedDonationEmail
        {
            DonorName = donorName,
            DonorEmail = donorEmail,
            GivingNumber = givingNumber,
            Amount = amount,
            ReferenceNumber = referenceNumber,
            RecipientEmailAddress = recipientEmail,
            GlobalMessageID = globalMessageID,
            EmailBlobID = emailBlobID,
            DateTimeReceived = receivedDate,
            FinancialInstitution = financialInstitution,
            DepositInstitution = depositInstitution,
            PaymentKey = paymentKey,
            PaymentNotificationID = paymentNotificationID,
            ShortComment = BuildShortComment(donorMessage, financialInstitution, depositInstitution)
        };
    }

    private static string? ExtractLineValue(string text, string label)
    {
        var match = Regex.Match(
            text,
            @$"^\s*{Regex.Escape(label)}:\s*(?<value>.+?)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Multiline);

        return match.Success
            ? NullIfWhiteSpace(match.Groups["value"].Value)
            : null;
    }

    private static decimal ExtractAmount(string text)
    {
        var amountLine = Regex.Match(
            text,
            @"^\s*Amount:\s*\$?\s*(?<amount>[\d,]+\.\d{2})",
            RegexOptions.IgnoreCase | RegexOptions.Multiline);

        var amountMatch = amountLine.Success
            ? amountLine
            : Regex.Match(text, @"\$\s*(?<amount>[\d,]+\.\d{2})");

        if (!amountMatch.Success)
        {
            throw new FormatException("Could not extract e-transfer amount from email.");
        }

        var amountText = amountMatch.Groups["amount"].Value.Replace(",", "");

        if (!decimal.TryParse(
                amountText,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var amount))
        {
            throw new FormatException($"Could not parse e-transfer amount '{amountText}'.");
        }

        if (amount <= 0)
        {
            throw new FormatException("E-transfer amount must be greater than 0.");
        }

        return amount;
    }

    private static string? ExtractFinancialInstitution(string text)
    {
        var match = Regex.Match(
            text,
            @"on behalf of\s+.+?\s+at\s+(?<bank>[^.\r\n]+)",
            RegexOptions.IgnoreCase);

        return match.Success
            ? NullIfWhiteSpace(match.Groups["bank"].Value)
            : null;
    }

    private static string? ExtractDepositInstitution(string text)
    {
        var match = Regex.Match(
            text,
            @"automatically deposited into your account at\s+(?<bank>[^.\r\n]+)",
            RegexOptions.IgnoreCase);

        return match.Success
            ? NullIfWhiteSpace(match.Groups["bank"].Value)
            : null;
    }

    private static string? ExtractGivingNumber(string text)
    {
        var match = Regex.Match(
            text,
            @"(?<![A-Za-z0-9])#?(?<gn>GN[A-Za-z0-9_-]+)\b",
            RegexOptions.IgnoreCase);

        return match.Success
            ? match.Groups["gn"].Value.ToUpperInvariant()
            : null;
    }

    private static string? GetMailboxName(MailboxAddress? mailbox)
    {
        return mailbox is null
            ? null
            : NullIfWhiteSpace(mailbox.Name);
    }

    private static string? GetUsableDonorEmail(string? email)
    {
        email = NullIfWhiteSpace(email)?.ToLowerInvariant();

        if (email is null)
        {
            return null;
        }

        if (!Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
        {
            return null;
        }

        // Interac/system sender emails are not donor emails.
        if (email.Contains("payments.interac.ca", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // RBC sometimes gives a generated reply address like ...@3m.rbc.com.
        // Treat that as a bank relay, not the donor's actual email.
        if (email.EndsWith(".rbc.com", StringComparison.OrdinalIgnoreCase) ||
            email.EndsWith("@rbc.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return email;
    }


    private static string? GetUsableRecipientEmail(string? email)
    {
        email = NullIfWhiteSpace(email)?.ToLowerInvariant();

        if (email is null)
        {
            return null;
        }

        return Regex.IsMatch(
            email,
            @"^[^@\s]+@[^@\s]+\.[^@\s]+$")
            ? email
            : null;
    }

    private static string? NormalizeMessageID(string? messageID)
    {
        messageID = NullIfWhiteSpace(messageID);

        if (messageID is null)
        {
            return null;
        }

        return messageID.StartsWith('<') && messageID.EndsWith('>')
            ? messageID
            : $"<{messageID}>";
    }

    private static string? BuildShortComment(
        string? donorMessage,
        string? sourceFinancialInstitution,
        string? depositInstitution)
    {
        var parts =
            new List<string>();

        if (!string.IsNullOrWhiteSpace(
                donorMessage))
        {
            parts.Add(
                donorMessage.Trim());
        }

        if (!string.IsNullOrWhiteSpace(
                sourceFinancialInstitution) ||
            !string.IsNullOrWhiteSpace(
                depositInstitution))
        {
            parts.Add(
                $"Source FI: {sourceFinancialInstitution ?? "Unknown"}; " +
                $"Deposit FI: {depositInstitution ?? "Unknown"}");
        }

        if (parts.Count == 0)
        {
            return null;
        }

        var comment =
            string.Join(
                "; ",
                parts);

        return comment.Length <= 255
            ? comment
            : comment[..255];
    }

    private static string HtmlToText(string html)
    {
        var decoded = WebUtility.HtmlDecode(html);
        var noTags = Regex.Replace(decoded, "<.*?>", " ");
        return Regex.Replace(noTags, @"\s+", " ").Trim();
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}