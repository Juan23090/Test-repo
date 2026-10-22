using System.Security.Cryptography;
using System.Text;

namespace EMA.Api.Services.Authentication;

public static class TrustedDeviceToken
{
    private const int TokenByteLength = 32;
    private const int EncodedTokenLength = 43;

    public static string Create()
    {
        var bytes =
            RandomNumberGenerator.GetBytes(
                TokenByteLength);

        return Convert
            .ToBase64String(
                bytes)
            .TrimEnd('=')
            .Replace(
                '+',
                '-')
            .Replace(
                '/',
                '_');
    }

    public static byte[] Hash(
        string token)
    {
        if (!IsWellFormed(
                token))
        {
            throw new ArgumentException(
                "The trusted-device token is invalid.",
                nameof(token));
        }

        return SHA256.HashData(
            Encoding.UTF8.GetBytes(
                token));
    }

    public static bool IsWellFormed(
        string? token)
    {
        return !string.IsNullOrWhiteSpace(
                   token) &&
               token.Length ==
               EncodedTokenLength &&
               token.All(
                   character =>
                       char.IsAsciiLetterOrDigit(
                           character) ||
                       character == '-' ||
                       character == '_');
    }
}
