namespace EMA.Api.Services;

public sealed class BlobGetResult
{
    public required Stream Content { get; init; }
    public required string ContentType { get; init; }
    public long? ContentLength { get; init; }
}