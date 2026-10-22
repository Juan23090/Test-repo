namespace EMA.Api.Services;

public sealed class GraphEmailMessage
{
    public required string Id { get; init; }

    public string? Subject { get; init; }

    public string? InternetMessageId { get; init; }

    public DateTimeOffset? ReceivedDateTime { get; init; }

    public string? FromAddress { get; init; }

    public string? FromName { get; init; }

    public string? RecipientEmailAddress { get; init; }

    public string? ReplyToAddress { get; init; }
}