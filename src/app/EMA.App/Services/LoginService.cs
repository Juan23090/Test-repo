using System.Net.Http.Json;
using EMA.App.Models;

namespace EMA.App.Services;

public sealed class LoginService : ILoginService
{
    private const string ApiBase =
        "/api";

    private readonly HttpClient _http;

    public LoginService(
        HttpClient http)
    {
        _http =
            http;
    }

    public async Task<LoginServiceResult<List<PublicOrganizationLookup>>> SearchPublicOrganizationsAsync(
        string term,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response =
                await _http.GetAsync(
                    $"{ApiBase}/public/organizations/search?term={Uri.EscapeDataString(term)}",
                    cancellationToken);

            return await ReadResultAsync<List<PublicOrganizationLookup>>(
                response,
                cancellationToken);
        }
        catch (Exception exception)
        {
            return LoginServiceResult<List<PublicOrganizationLookup>>.Failure(
                $"Unable to search organizations: {exception.Message}");
        }
    }

    public async Task<LoginServiceResult<DonorOrganizationLoginLookupResponse>> LookupDonorForOrganizationAsync(
        int EMAOrganizationID,
        string phoneNumber,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response =
                await _http.PostAsJsonAsync(
                    $"{ApiBase}/donorlogin/{EMAOrganizationID}/lookup",
                    new DonorOrganizationLoginLookupRequest
                    {
                        PhoneNumber =
                            phoneNumber
                    },
                    cancellationToken);

            return await ReadResultAsync<DonorOrganizationLoginLookupResponse>(
                response,
                cancellationToken);
        }
        catch (Exception exception)
        {
            return LoginServiceResult<DonorOrganizationLoginLookupResponse>.Failure(
                $"Unable to look up donor login: {exception.Message}");
        }
    }

    public async Task<LoginServiceResult<StartDonorSelfRegistrationVerificationResponse>> StartDonorSelfRegistrationVerificationAsync(
        StartDonorSelfRegistrationVerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response =
                await _http.PostAsJsonAsync(
                    $"{ApiBase}/donor-registration/start-verification",
                    request,
                    cancellationToken);

            return await ReadResultAsync<StartDonorSelfRegistrationVerificationResponse>(
                response,
                cancellationToken);
        }
        catch (Exception exception)
        {
            return LoginServiceResult<StartDonorSelfRegistrationVerificationResponse>.Failure(
                $"Unable to start donor registration verification: {exception.Message}");
        }
    }

    public async Task<LoginServiceResult<LoginResponse>> VerifyDonorSelfRegistrationAsync(
        string registrationID,
        string code,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response =
                await _http.PostAsJsonAsync(
                    $"{ApiBase}/donor-registration/verify",
                    new VerifyDonorSelfRegistrationRequest
                    {
                        RegistrationID =
                            registrationID,

                        Code =
                            code
                    },
                    cancellationToken);

            return await ReadResultAsync<LoginResponse>(
                response,
                cancellationToken);
        }
        catch (Exception exception)
        {
            return LoginServiceResult<LoginResponse>.Failure(
                $"Unable to verify donor registration: {exception.Message}");
        }
    }

    public async Task<LoginServiceResult<LoginVerificationStartResponse>> LookupUnifiedLoginAsync(
        string phoneNumber,
        string emailAddress,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response =
                await _http.PostAsJsonAsync(
                    $"{ApiBase}/login/unified-lookup",
                    new UnifiedLoginLookupRequest
                    {
                        Identifier =
                            phoneNumber,

                        EmailAddress =
                            emailAddress
                    },
                    cancellationToken);

            return await ReadResultAsync<LoginVerificationStartResponse>(
                response,
                cancellationToken);
        }
        catch (Exception exception)
        {
            return LoginServiceResult<LoginVerificationStartResponse>.Failure(
                $"Unable to connect to the login service: {exception.Message}");
        }
    }

    public async Task<LoginServiceResult<UnifiedLoginLookupResponse>> VerifyUnifiedLoginCodeAsync(
        string phoneNumber,
        string emailAddress,
        string verificationID,
        string code,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response =
                await _http.PostAsJsonAsync(
                    $"{ApiBase}/login/unified-verify",
                    new VerifyUnifiedLoginCodeRequest
                    {
                        PhoneNumber =
                            phoneNumber,

                        EmailAddress =
                            emailAddress,

                        VerificationID =
                            verificationID,

                        Code =
                            code
                    },
                    cancellationToken);

            return await ReadResultAsync<UnifiedLoginLookupResponse>(
                response,
                cancellationToken);
        }
        catch (Exception exception)
        {
            return LoginServiceResult<UnifiedLoginLookupResponse>.Failure(
                $"Unable to verify login code: {exception.Message}");
        }
    }

    public async Task<LoginServiceResult<LoginResponse>> CompleteUnifiedLoginAsync(
        string phoneNumber,
        string emailAddress,
        string verificationID,
        string actorType,
        int EMAOrganizationID,
        int? userID,
        int? donorID,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response =
                await _http.PostAsJsonAsync(
                    $"{ApiBase}/login/unified-complete",
                    new CompleteUnifiedLoginRequest
                    {
                        PhoneNumber =
                            phoneNumber,

                        EmailAddress =
                            emailAddress,

                        VerificationID =
                            verificationID,

                        ActorType =
                            actorType,

                        EMAOrganizationID =
                            EMAOrganizationID,

                        UserID =
                            userID,

                        DonorID =
                            donorID
                    },
                    cancellationToken);

            return await ReadResultAsync<LoginResponse>(
                response,
                cancellationToken);
        }
        catch (Exception exception)
        {
            return LoginServiceResult<LoginResponse>.Failure(
                $"Unable to complete login: {exception.Message}");
        }
    }

    public async Task<LoginServiceResult<LoginVerificationStartResponse>> StartOrganizationLoginVerificationAsync(
        int EMAOrganizationID,
        int userID,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response =
                await _http.PostAsJsonAsync(
                    $"{ApiBase}/login/start-verification",
                    new StartOrganizationLoginVerificationRequest
                    {
                        EMAOrganizationID =
                            EMAOrganizationID,

                        UserID =
                            userID
                    },
                    cancellationToken);

            return await ReadResultAsync<LoginVerificationStartResponse>(
                response,
                cancellationToken);
        }
        catch (Exception exception)
        {
            return LoginServiceResult<LoginVerificationStartResponse>.Failure(
                $"Unable to start login verification: {exception.Message}");
        }
    }

    public async Task<LoginServiceResult<LoginVerificationStartResponse>> StartDonorLoginVerificationAsync(
        int EMAOrganizationID,
        int donorID,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response =
                await _http.PostAsJsonAsync(
                    $"{ApiBase}/donor-login/start-verification",
                    new StartDonorLoginVerificationRequest
                    {
                        EMAOrganizationID =
                            EMAOrganizationID,

                        DonorID =
                            donorID
                    },
                    cancellationToken);

            return await ReadResultAsync<LoginVerificationStartResponse>(
                response,
                cancellationToken);
        }
        catch (Exception exception)
        {
            return LoginServiceResult<LoginVerificationStartResponse>.Failure(
                $"Unable to start donor login verification: {exception.Message}");
        }
    }

    public async Task<LoginServiceResult<LoginLookupResponse>> LookupUserAsync(
        string identifier,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response =
                await _http.PostAsJsonAsync(
                    $"{ApiBase}/login/lookup",
                    new LoginLookupRequest
                    {
                        Identifier =
                            identifier
                    },
                    cancellationToken);

            return await ReadResultAsync<LoginLookupResponse>(
                response,
                cancellationToken);
        }
        catch (Exception exception)
        {
            return LoginServiceResult<LoginLookupResponse>.Failure(
                $"Unable to connect to the login service: {exception.Message}");
        }
    }

    public async Task<LoginServiceResult<LoginResponse>> LoginAsync(
        int EMAOrganizationID,
        int userID,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response =
                await _http.PostAsJsonAsync(
                    $"{ApiBase}/login",
                    new LoginRequest
                    {
                        EMAOrganizationID =
                            EMAOrganizationID,

                        UserID =
                            userID
                    },
                    cancellationToken);

            return await ReadResultAsync<LoginResponse>(
                response,
                cancellationToken);
        }
        catch (Exception exception)
        {
            return LoginServiceResult<LoginResponse>.Failure(
                $"Unable to complete login: {exception.Message}");
        }
    }

    public async Task<LoginServiceResult<DonorLoginLookupResponse>> LookupDonorAsync(
        string identifier,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response =
                await _http.PostAsJsonAsync(
                    $"{ApiBase}/donor-login/lookup",
                    new DonorLoginLookupRequest
                    {
                        Identifier =
                            identifier
                    },
                    cancellationToken);

            return await ReadResultAsync<DonorLoginLookupResponse>(
                response,
                cancellationToken);
        }
        catch (Exception exception)
        {
            return LoginServiceResult<DonorLoginLookupResponse>.Failure(
                $"Unable to connect to the donor login service: {exception.Message}");
        }
    }

    public async Task<LoginServiceResult<LoginResponse>> DonorLoginAsync(
        int EMAOrganizationID,
        int donorID,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response =
                await _http.PostAsJsonAsync(
                    $"{ApiBase}/donor-login",
                    new DonorLoginRequest
                    {
                        EMAOrganizationID =
                            EMAOrganizationID,

                        DonorID =
                            donorID
                    },
                    cancellationToken);

            return await ReadResultAsync<LoginResponse>(
                response,
                cancellationToken);
        }
        catch (Exception exception)
        {
            return LoginServiceResult<LoginResponse>.Failure(
                $"Unable to complete donor login: {exception.Message}");
        }
    }

    public async Task<LoginServiceResult<LoginResponse>> VerifyOrganizationLoginCodeAsync(
        int EMAOrganizationID,
        int userID,
        string verificationID,
        string code,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response =
                await _http.PostAsJsonAsync(
                    $"{ApiBase}/login/verify-code",
                    new VerifyOrganizationLoginCodeRequest
                    {
                        EMAOrganizationID =
                            EMAOrganizationID,

                        UserID =
                            userID,

                        VerificationID =
                            verificationID,

                        Code =
                            code
                    },
                    cancellationToken);

            return await ReadResultAsync<LoginResponse>(
                response,
                cancellationToken);
        }
        catch (Exception exception)
        {
            return LoginServiceResult<LoginResponse>.Failure(
                $"Unable to verify login code: {exception.Message}");
        }
    }

    public async Task<LoginServiceResult<LoginResponse>> VerifyDonorLoginCodeAsync(
        int EMAOrganizationID,
        int donorID,
        string verificationID,
        string code,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response =
                await _http.PostAsJsonAsync(
                    $"{ApiBase}/donor-login/verify-code",
                    new VerifyDonorLoginCodeRequest
                    {
                        EMAOrganizationID =
                            EMAOrganizationID,

                        DonorID =
                            donorID,

                        VerificationID =
                            verificationID,

                        Code =
                            code
                    },
                    cancellationToken);

            return await ReadResultAsync<LoginResponse>(
                response,
                cancellationToken);
        }
        catch (Exception exception)
        {
            return LoginServiceResult<LoginResponse>.Failure(
                $"Unable to verify donor login code: {exception.Message}");
        }
    }

    private static async Task<LoginServiceResult<T>> ReadResultAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            var value =
                await response.Content.ReadFromJsonAsync<T>(
                    cancellationToken:
                        cancellationToken);

            return value is null
                ? LoginServiceResult<T>.Failure(
                    "The server returned an empty response.")
                : LoginServiceResult<T>.Success(
                    value);
        }

        var error =
            await response.Content.ReadFromJsonAsync<LoginApiError>(
                cancellationToken:
                    cancellationToken);

        return LoginServiceResult<T>.Failure(
            string.IsNullOrWhiteSpace(
                error?.Message)
                ? $"The request failed with status {(int)response.StatusCode}."
                : error.Message);
    }
}
