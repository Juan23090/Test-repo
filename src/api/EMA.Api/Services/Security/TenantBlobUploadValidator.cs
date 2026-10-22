using Microsoft.AspNetCore.Http;

namespace EMA.Api.Services.Security;

public static class TenantBlobUploadValidator
{
    public const long MaximumUploadBytes =
        50L * 1024 * 1024;

    private const long MaximumImageBytes =
        2L * 1024 * 1024;

    private const int InspectionBytes =
        512;

    public static async Task<
        ValidatedBlobUpload> ValidateAsync(
            HttpRequest request,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            request);

        var contentType =
            NormalizeContentType(
                request.ContentType);

        var maximumBytes =
            GetMaximumBytes(
                contentType);

        if (request.ContentLength >
            maximumBytes)
        {
            throw new BlobUploadTooLargeException(
                maximumBytes);
        }

        var inspectionBuffer =
            new byte[InspectionBytes];

        var bytesRead =
            0;

        while (bytesRead <
               inspectionBuffer.Length)
        {
            var read =
                await request.Body.ReadAsync(
                    inspectionBuffer.AsMemory(
                        bytesRead),
                    cancellationToken);

            if (read == 0)
            {
                break;
            }

            bytesRead +=
                read;
        }

        if (bytesRead == 0)
        {
            throw new ArgumentException(
                "The uploaded file is empty.");
        }

        if (!MatchesContentType(
                contentType,
                inspectionBuffer,
                bytesRead))
        {
            throw new ArgumentException(
                "The uploaded file content does not match its Content-Type.");
        }

        return new ValidatedBlobUpload(
            contentType,
            new PrefixBoundedReadStream(
                inspectionBuffer.AsMemory(
                        0,
                        bytesRead)
                    .ToArray(),
                request.Body,
                maximumBytes));
    }

    private static long GetMaximumBytes(
        string contentType)
    {
        return contentType switch
        {
            "image/png" or
            "image/jpeg" or
            "image/gif" or
            "image/webp" =>
                MaximumImageBytes,

            "text/csv" or
            "application/csv" or
            "application/vnd.ms-excel" or
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" =>
                MaximumUploadBytes,

            _ => throw new ArgumentException(
                "Only PNG, JPEG, GIF, WEBP, CSV, XLS, and XLSX uploads are supported.")
        };
    }

    private static string NormalizeContentType(
        string? contentType)
    {
        if (string.IsNullOrWhiteSpace(
                contentType))
        {
            throw new ArgumentException(
                "Content-Type header is required.");
        }

        return contentType
            .Split(
                ';',
                2,
                StringSplitOptions.TrimEntries)[0]
            .ToLowerInvariant();
    }

    private static bool MatchesContentType(
        string contentType,
        byte[] buffer,
        int bytesRead)
    {
        var content =
            buffer.AsSpan(
                0,
                bytesRead);

        return contentType switch
        {
            "image/png" =>
                content.StartsWith(
                    new byte[]
                    {
                        0x89, 0x50, 0x4E, 0x47,
                        0x0D, 0x0A, 0x1A, 0x0A
                    }),

            "image/jpeg" =>
                content.Length >= 3 &&
                content[0] == 0xFF &&
                content[1] == 0xD8 &&
                content[2] == 0xFF,

            "image/gif" =>
                content.StartsWith(
                    "GIF87a"u8) ||
                content.StartsWith(
                    "GIF89a"u8),

            "image/webp" =>
                content.Length >= 12 &&
                content[..4].SequenceEqual(
                    "RIFF"u8) &&
                content.Slice(
                        8,
                        4)
                    .SequenceEqual(
                        "WEBP"u8),

            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" =>
                content.Length >= 4 &&
                content[0] == 0x50 &&
                content[1] == 0x4B &&
                content[2] == 0x03 &&
                content[3] == 0x04,

            "application/vnd.ms-excel" =>
                content.StartsWith(
                    new byte[]
                    {
                        0xD0, 0xCF, 0x11, 0xE0,
                        0xA1, 0xB1, 0x1A, 0xE1
                    }),

            "text/csv" or
            "application/csv" =>
                !content.Contains(
                    (byte)0),

            _ => false
        };
    }
}

public sealed record ValidatedBlobUpload(
    string ContentType,
    Stream Content);

public sealed class BlobUploadTooLargeException :
    IOException
{
    public BlobUploadTooLargeException(
        long maximumBytes)
        : base(
            $"The uploaded file exceeds the {maximumBytes} byte limit.")
    {
    }
}

internal sealed class PrefixBoundedReadStream :
    Stream
{
    private readonly byte[] _prefix;
    private readonly Stream _inner;
    private readonly long _maximumBytes;

    private int _prefixOffset;
    private long _bytesReturned;

    public PrefixBoundedReadStream(
        byte[] prefix,
        Stream inner,
        long maximumBytes)
    {
        _prefix =
            prefix;

        _inner =
            inner;

        _maximumBytes =
            maximumBytes;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;

    public override long Length =>
        throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(
        byte[] buffer,
        int offset,
        int count)
    {
        return ReadCore(
            buffer.AsSpan(
                offset,
                count));
    }

    public override int Read(
        Span<byte> buffer)
    {
        return ReadCore(
            buffer);
    }

    public override async ValueTask<int>
        ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
    {
        if (_prefixOffset <
            _prefix.Length)
        {
            return CopyPrefix(
                buffer.Span);
        }

        if (_bytesReturned >=
            _maximumBytes)
        {
            var extraByte =
                new byte[1];

            var extraRead =
                await _inner.ReadAsync(
                    extraByte,
                    cancellationToken);

            if (extraRead > 0)
            {
                throw new BlobUploadTooLargeException(
                    _maximumBytes);
            }

            return 0;
        }

        var allowed =
            (int)Math.Min(
                buffer.Length,
                _maximumBytes -
                _bytesReturned);

        var read =
            await _inner.ReadAsync(
                buffer[..allowed],
                cancellationToken);

        _bytesReturned +=
            read;

        return read;
    }

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        return ReadAsync(
                buffer.AsMemory(
                    offset,
                    count),
                cancellationToken)
            .AsTask();
    }

    private int ReadCore(
        Span<byte> buffer)
    {
        if (_prefixOffset <
            _prefix.Length)
        {
            return CopyPrefix(
                buffer);
        }

        if (_bytesReturned >=
            _maximumBytes)
        {
            if (_inner.ReadByte() >= 0)
            {
                throw new BlobUploadTooLargeException(
                    _maximumBytes);
            }

            return 0;
        }

        var allowed =
            (int)Math.Min(
                buffer.Length,
                _maximumBytes -
                _bytesReturned);

        var read =
            _inner.Read(
                buffer[..allowed]);

        _bytesReturned +=
            read;

        return read;
    }

    private int CopyPrefix(
        Span<byte> destination)
    {
        var count =
            Math.Min(
                destination.Length,
                _prefix.Length -
                _prefixOffset);

        _prefix.AsSpan(
                _prefixOffset,
                count)
            .CopyTo(
                destination);

        _prefixOffset +=
            count;

        _bytesReturned +=
            count;

        return count;
    }

    public override void Flush()
    {
    }

    public override long Seek(
        long offset,
        SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(
        long value) =>
        throw new NotSupportedException();

    public override void Write(
        byte[] buffer,
        int offset,
        int count) =>
        throw new NotSupportedException();
}