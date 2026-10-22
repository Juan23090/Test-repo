using System.Net.Http.Headers;
using Microsoft.AspNetCore.Components.Forms;

namespace EMA.App.Services;

public class BlobService : IBlobService
{
    private const string ApiBase = "/api";

    private readonly HttpClient _http;

    public BlobService(HttpClient http)
    {
        _http = http;
    }

    public async Task<bool> BlobPutAsync(
        string blobID,
        IBrowserFile file,
        long maxAllowedSize,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(blobID) || file is null || file.Size == 0)
            {
                return false;
            }

            await using var fileStream = file.OpenReadStream(
                maxAllowedSize,
                cancellationToken);

            using var content = new StreamContent(fileStream);
            content.Headers.ContentType = new MediaTypeHeaderValue(
                string.IsNullOrWhiteSpace(file.ContentType)
                    ? "application/octet-stream"
                    : file.ContentType);

            var response = await _http.PutAsync(
                BuildBlobUrl(blobID),
                content,
                cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string?> BlobGetDataUrlAsync(
        string blobID,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(blobID))
            {
                return null;
            }

            var response = await _http.GetAsync(
                BuildBlobUrl(blobID),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var contentType = response.Content.Headers.ContentType?.MediaType;

            if (string.IsNullOrWhiteSpace(contentType))
            {
                contentType = "application/octet-stream";
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

            if (bytes.Length == 0)
            {
                return null;
            }

            return $"data:{contentType};base64,{Convert.ToBase64String(bytes)}";
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> BlobDeleteAsync(
        string blobID,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(blobID))
            {
                return false;
            }

            var response = await _http.DeleteAsync(
                BuildBlobUrl(blobID),
                cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildBlobUrl(string blobID)
        => $"{ApiBase}/blob/{Uri.EscapeDataString(blobID.Trim())}";
}