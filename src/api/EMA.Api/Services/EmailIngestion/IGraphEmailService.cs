namespace EMA.Api.Services;

public interface IGraphEmailService
{
    Task<IReadOnlyList<GraphEmailMessage>> GetCandidateMessagesAsync(
        CancellationToken cancellationToken = default);

    Task<byte[]> GetRawMimeAsync(
        string messageID,
        CancellationToken cancellationToken = default);

    Task MoveMessageAsync(
        string messageID,
        string destinationFolderName,
        CancellationToken cancellationToken = default);
}