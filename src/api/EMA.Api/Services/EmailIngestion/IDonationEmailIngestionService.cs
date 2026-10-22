namespace EMA.Api.Services;

public interface IDonationEmailIngestionService
{
    Task<DonationIngestionResult> IngestAsync(
        GraphEmailMessage graphMessage,
        byte[] rawMime,
        CancellationToken cancellationToken = default);
}