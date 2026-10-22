namespace EMA.Api.Services;

public sealed class ParsedDonationEmail
{
    public string? DonorName { get; init; }

    public string? DonorEmail { get; init; }

    public string? GivingNumber { get; init; }

    public decimal Amount { get; init; }

    public string? ReferenceNumber { get; init; }

    public string? RecipientEmailAddress { get; init; }

    public string? GlobalMessageID { get; init; }

    public string EmailBlobID { get; init; } = string.Empty;

    public DateTimeOffset DateTimeReceived { get; init; }

    public string? FinancialInstitution { get; init; }

    public string? DepositInstitution { get; init; }

    public string? PaymentKey { get; init; }

    public string? PaymentNotificationID { get; init; }

    public string? ShortComment { get; init; }
}