namespace EMA.Api.Services.Audio;

public interface ITextToSpeechService
{
    Task<GeneratedSpeechAudio> GenerateMp3Async(
        string text,
        CancellationToken cancellationToken = default);
}

public sealed record GeneratedSpeechAudio(
    byte[] Content,
    string ContentType);
