using EMA.Api.Services;
using EMA.Api.Services.Email;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace EMA_Api.Functions.Email;

public sealed class IngestEtransferEmails
{
    private static readonly SemaphoreSlim RunLock =
        new(
            1,
            1);

    private readonly ILogger<IngestEtransferEmails> _logger;
    private readonly IGraphEmailService _graphEmailService;

    private readonly IDonationEmailIngestionService
        _donationEmailIngestionService;

    private readonly IEmailNotificationService
        _emailNotificationService;

    public IngestEtransferEmails(
        ILogger<IngestEtransferEmails> logger,
        IGraphEmailService graphEmailService,
        IDonationEmailIngestionService
            donationEmailIngestionService,
        IEmailNotificationService
            emailNotificationService)
    {
        _logger =
            logger;

        _graphEmailService =
            graphEmailService;

        _donationEmailIngestionService =
            donationEmailIngestionService;

        _emailNotificationService =
            emailNotificationService;
    }

    [Function("IngestEtransferEmails")]
    public async Task Run(
        [TimerTrigger("%EtransferIngestSchedule%")]
        TimerInfo timer,
        CancellationToken cancellationToken)
    {
        if (!await RunLock.WaitAsync(
                0,
                cancellationToken))
        {
            _logger.LogWarning(
                "IngestEtransferEmails skipped because another run is already active in this host.");

            return;
        }

        try
        {
            await RunInternalAsync(
                cancellationToken);
        }
        finally
        {
            RunLock.Release();
        }
    }

    private async Task RunInternalAsync(
        CancellationToken cancellationToken)
    {
        var processedFolderName =
            Environment.GetEnvironmentVariable(
                "EtransferProcessedFolderName")
            ?? "EMA Processed";

        var failedFolderName =
            Environment.GetEnvironmentVariable(
                "EtransferFailedFolderName")
            ?? "EMA Failed";

        _logger.LogInformation(
            "IngestEtransferEmails started at {UtcNow}.",
            DateTimeOffset.UtcNow);

        var messages =
            await _graphEmailService
                .GetCandidateMessagesAsync(
                    cancellationToken);

        _logger.LogInformation(
            "Found {Count} inbox email(s) to inspect.",
            messages.Count);

        foreach (var message in messages)
        {
            try
            {
                _logger.LogInformation(
                    "Processing email {MessageID}: {Subject}",
                    message.Id,
                    message.Subject);

                var rawMime =
                    await _graphEmailService
                        .GetRawMimeAsync(
                            message.Id,
                            cancellationToken);

                var result =
                    await _donationEmailIngestionService
                        .IngestAsync(
                            message,
                            rawMime,
                            cancellationToken);

                if (result.WasDuplicate)
                {
                    _logger.LogInformation(
                        "Email was already ingested. DonationID={DonationID}, DonorID={DonorID}, StepID={DonationStepID}. Moving to processed folder.",
                        result.NewDonationID,
                        result.EMADonorID,
                        result.DonationStepID);

                    await _graphEmailService
                        .MoveMessageAsync(
                            message.Id,
                            processedFolderName,
                            cancellationToken);

                    continue;
                }

                try
                {
                    await _emailNotificationService
                        .SendDonationStepEmailsAsync(
                            message,
                            result,
                            cancellationToken);
                }
                catch (Exception emailEx)
                {
                    _logger.LogError(
                        emailEx,
                        "Donation was ingested, but notification email sending failed. DonationID={DonationID}, StepID={DonationStepID}.",
                        result.NewDonationID,
                        result.DonationStepID);
                }

                if (!result.WasSuccessful)
                {
                    _logger.LogWarning(
                        "Email was logged but not fully ingested. DonationID={DonationID}, DonorID={DonorID}, StepID={DonationStepID}. Moving to failed folder.",
                        result.NewDonationID,
                        result.EMADonorID,
                        result.DonationStepID);

                    await _graphEmailService
                        .MoveMessageAsync(
                            message.Id,
                            failedFolderName,
                            cancellationToken);

                    continue;
                }

                _logger.LogInformation(
                    "Email processed successfully. DonationID={DonationID}, DonorID={DonorID}, WasDonorCreated={WasDonorCreated}, WasDuplicate={WasDuplicate}, StepID={DonationStepID}.",
                    result.NewDonationID,
                    result.EMADonorID,
                    result.WasDonorCreated,
                    result.WasDuplicate,
                    result.DonationStepID);

                await _graphEmailService
                    .MoveMessageAsync(
                        message.Id,
                        processedFolderName,
                        cancellationToken);
            }
            catch (GraphEmailMessageNotFoundException)
            {
                _logger.LogWarning(
                    "Skipping email {MessageID}: {Subject} because it is no longer available in the mailbox. Another ingestion instance may have already handled it.",
                    message.Id,
                    message.Subject);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to process email {MessageID}: {Subject}",
                    message.Id,
                    message.Subject);

                try
                {
                    await _graphEmailService
                        .MoveMessageAsync(
                            message.Id,
                            failedFolderName,
                            cancellationToken);
                }
                catch (GraphEmailMessageNotFoundException)
                {
                    _logger.LogWarning(
                        "Failed email {MessageID} was not moved because it is no longer available in the mailbox.",
                        message.Id);
                }
                catch (Exception moveEx)
                {
                    _logger.LogError(
                        moveEx,
                        "Failed to move failed email {MessageID} to failed folder.",
                        message.Id);
                }
            }
        }

        _logger.LogInformation(
            "IngestEtransferEmails finished at {UtcNow}.",
            DateTimeOffset.UtcNow);
    }
}