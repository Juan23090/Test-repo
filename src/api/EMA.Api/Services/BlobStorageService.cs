using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace EMA.Api.Services;

public sealed class BlobStorageService : IBlobStorageService
{
    private const string DefaultContainerName = "ema-blobs";

    private readonly BlobContainerClient _containerClient;

    public BlobStorageService()
    {
        var connectionString =
            Environment.GetEnvironmentVariable("BlobStorageConnectionString")
            ?? Environment.GetEnvironmentVariable("AzureWebJobsStorage")
            ?? throw new InvalidOperationException("Missing BlobStorageConnectionString or AzureWebJobsStorage.");

        var containerName =
            Environment.GetEnvironmentVariable("BlobContainerName")
            ?? DefaultContainerName;

        _containerClient = new BlobContainerClient(connectionString, containerName);
    }

    public async Task BlobPutAsync(
        string blobID,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        blobID = ValidateBlobID(blobID);

        if (content is null)
        {
            throw new ArgumentNullException(nameof(content));
        }

        if (!content.CanRead)
        {
            throw new ArgumentException("The provided stream must be readable.", nameof(content));
        }

        if (string.IsNullOrWhiteSpace(contentType))
        {
            throw new ArgumentException("Content type is required.", nameof(contentType));
        }

        await _containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var blobClient = _containerClient.GetBlobClient(blobID);

        if (content.CanSeek)
        {
            content.Position = 0;
        }

        // Force overwrite. This is important because organization logos keep the same LogoBlobID.
        await blobClient.UploadAsync(
            content,
            overwrite: true,
            cancellationToken: cancellationToken);

        await blobClient.SetHttpHeadersAsync(
            new BlobHttpHeaders
            {
                ContentType = contentType
            },
            cancellationToken: cancellationToken);
    }

    public async Task<BlobGetResult> BlobGetAsync(
        string blobID,
        CancellationToken cancellationToken = default)
    {
        blobID = ValidateBlobID(blobID);

        var blobClient = _containerClient.GetBlobClient(blobID);

        try
        {
            var response = await blobClient.DownloadStreamingAsync(
                cancellationToken: cancellationToken);

            return new BlobGetResult
            {
                Content = response.Value.Content,
                ContentType = response.Value.Details.ContentType ?? "application/octet-stream",
                ContentLength = response.Value.Details.ContentLength
            };
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new FileNotFoundException($"Blob not found: {blobID}", ex);
        }
    }

    public async Task<bool> BlobDeleteAsync(
        string blobID,
        CancellationToken cancellationToken = default)
    {
        blobID = ValidateBlobID(blobID);

        var blobClient = _containerClient.GetBlobClient(blobID);

        var response = await blobClient.DeleteIfExistsAsync(
            DeleteSnapshotsOption.IncludeSnapshots,
            conditions: null,
            cancellationToken: cancellationToken);

        return response.Value;
    }

    private static string ValidateBlobID(string blobID)
    {
        if (string.IsNullOrWhiteSpace(blobID))
        {
            throw new ArgumentException("BlobID is required.", nameof(blobID));
        }

        blobID = blobID.Trim();

        // Keep this route-safe. If you later want folders like organizations/{id}/logo.png,
        // change the function routes to catch-all routes first: blob/{*blobID}.
        if (blobID.Contains('/') || blobID.Contains('\\'))
        {
            throw new ArgumentException("BlobID cannot contain slashes.", nameof(blobID));
        }

        if (blobID.Length > 200)
        {
            throw new ArgumentException("BlobID must be 200 characters or fewer.", nameof(blobID));
        }

        foreach (var c in blobID)
        {
            if (!char.IsLetterOrDigit(c) && c is not '-' and not '_' and not '.')
            {
                throw new ArgumentException("BlobID can only contain letters, numbers, hyphens, underscores, and periods.", nameof(blobID));
            }
        }

        return blobID;
    }
}