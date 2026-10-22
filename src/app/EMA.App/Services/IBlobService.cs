using Microsoft.AspNetCore.Components.Forms;

namespace EMA.App.Services;

public interface IBlobService
{
    Task<bool> BlobPutAsync(
        string blobID,
        IBrowserFile file,
        long maxAllowedSize,
        CancellationToken cancellationToken = default);

    Task<string?> BlobGetDataUrlAsync(
        string blobID,
        CancellationToken cancellationToken = default);

    Task<bool> BlobDeleteAsync(
        string blobID,
        CancellationToken cancellationToken = default);
}