namespace EMA.Api.Services.Security;

public interface IApiAccessTokenService
{
    ApiAccessToken CreateToken(
        ApiRequestIdentity identity);

    bool TryValidateToken(
        string token,
        out ApiRequestIdentity? identity);
}

public sealed record ApiAccessToken(
    string Token,
    DateTimeOffset ExpiresAtUtc);
