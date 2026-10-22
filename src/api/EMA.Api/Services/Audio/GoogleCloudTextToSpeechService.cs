using System.Text;
using Google.Cloud.TextToSpeech.V1;
using Microsoft.Extensions.Configuration;

namespace EMA.Api.Services.Audio;

public sealed class GoogleCloudTextToSpeechService : ITextToSpeechService
{
    private const int MaxSynchronousInputBytes = 5000;
    private const string DefaultLanguageCode = "en-US";
    private const string DefaultVoiceName = "en-US-Neural2-F";

    private readonly Lazy<Task<TextToSpeechClient>> _client;
    private readonly string _languageCode;
    private readonly string _voiceName;

    public GoogleCloudTextToSpeechService(IConfiguration configuration)
    {
        _languageCode =
            configuration["GoogleTtsLanguageCode"]
            ?? Environment.GetEnvironmentVariable("GoogleTtsLanguageCode")
            ?? DefaultLanguageCode;

        _voiceName =
            configuration["GoogleTtsVoiceName"]
            ?? Environment.GetEnvironmentVariable("GoogleTtsVoiceName")
            ?? DefaultVoiceName;

        _client = new Lazy<Task<TextToSpeechClient>>(
            () => TextToSpeechClient.CreateAsync());
    }

    public async Task<GeneratedSpeechAudio> GenerateMp3Async(
        string text,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException(
                "Audio Script is required.",
                nameof(text));
        }

        var script = text.Trim();

        if (Encoding.UTF8.GetByteCount(script) > MaxSynchronousInputBytes)
        {
            throw new ArgumentException(
                "Audio Script is too long for a single Google Cloud Text-to-Speech request. Keep it under 5,000 UTF-8 bytes.",
                nameof(text));
        }

        var client = await _client.Value;

        var response = await client.SynthesizeSpeechAsync(
            new SynthesisInput
            {
                Text = script
            },
            new VoiceSelectionParams
            {
                LanguageCode = _languageCode,
                Name = _voiceName,
                SsmlGender = SsmlVoiceGender.Female
            },
            new AudioConfig
            {
                AudioEncoding = AudioEncoding.Mp3
            },
            cancellationToken);

        var bytes = response.AudioContent.ToByteArray();

        if (bytes.Length == 0)
        {
            throw new InvalidOperationException(
                "Google Cloud Text-to-Speech returned an empty recording.");
        }

        return new GeneratedSpeechAudio(
            bytes,
            "audio/mpeg");
    }
}
