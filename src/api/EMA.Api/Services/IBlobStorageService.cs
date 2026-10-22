namespace EMA.Api.Services;

public interface IBlobStorageService
{
    Task BlobPutAsync(
        string blobID,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default);

    Task<BlobGetResult> BlobGetAsync(
        string blobID,
        CancellationToken cancellationToken = default);

    Task<bool> BlobDeleteAsync(
        string blobID,
        CancellationToken cancellationToken = default);
}