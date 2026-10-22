using EMA.Api.Services;

namespace EMA.Api.Services.Email;

public enum DonationTransactionType
{
    Manual,
    Automated
}

public enum EmailDeliveryStatus
{
    NotRequested,
    Queued,
    Skipped,
    Failed
}

public sealed record EmailDeliveryResult(
    EmailDeliveryStatus Status,
    string? Message = null)
{
    public static EmailDeliveryResult NotRequested() =>
        new(
            EmailDeliveryStatus.NotRequested);

    public static EmailDeliveryResult Queued(
        string? message = null) =>
        new(
            EmailDeliveryStatus.Queued,
            message);

    public static EmailDeliveryResult Skipped(
        string message) =>
        new(
            EmailDeliveryStatus.Skipped,
            message);

    public static EmailDeliveryResult Failed(
        string message) =>
        new(
            EmailDeliveryStatus.Failed,
            message);
}

public sealed record DonationStepEmailResult(
    EmailDeliveryResult Donor,
    EmailDeliveryResult Organization,
    EmailDeliveryResult EmaSupport);

public interface IEmailNotificationService
{
    Task<DonationStepEmailResult> SendDonationStepEmailsAsync(
        GraphEmailMessage sourceMessage,
        DonationIngestionResult ingestionResult,
        CancellationToken cancellationToken = default);

    Task<EmailDeliveryResult> SendDonationAcknowledgementAsync(
        long donationID,
        DonationTransactionType transactionType,
        CancellationToken cancellationToken = default);
}
