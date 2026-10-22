namespace EMA.Api.Services.Authentication;

public sealed class TwoFactorCodeRateLimitException : Exception
{
    public TwoFactorCodeRateLimitException(
        string message,
        TimeSpan retryAfter)
        : base(message)
    {
        RetryAfter = retryAfter;
    }

    public TimeSpan RetryAfter { get; }
}