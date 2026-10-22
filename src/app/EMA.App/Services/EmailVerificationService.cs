using System.Net.Http.Json;
using EMA.App.Models;

namespace EMA.App.Services;

public sealed class EmailVerificationService :
    IEmailVerificationService
{
    private const string ApiBase =
        "/api/email-verification";

    private const string PublicOrganizationApiBase =
        "/api/public/onboarding/email-verification";

    private readonly HttpClient _http;

    public EmailVerificationService(
        HttpClient http)
    {
        _http =
            http;
    }

    public async Task<
        EmailVerificationServiceResult<StartEmailVerificationResponse>>
        StartAsync(
            EmailVerificationPurpose purpose,
            int actorID,
            string emailAddress,
            CancellationToken cancellationToken = default)
    {
        try
        {
            var response =
                await _http.PostAsJsonAsync(
                    GetApiBase(purpose, actorID) + "/start",
                    new StartEmailVerificationRequest
                    {
                        Purpose =
                            purpose,

                        ActorID =
                            actorID,

                        EmailAddress =
                            emailAddress
                    },
                    cancellationToken);

            return await ReadResultAsync<
                StartEmailVerificationResponse>(
                    response,
                    cancellationToken);
        }
        catch (Exception exception)
        {
            return EmailVerificationServiceResult<
                StartEmailVerificationResponse>.Failure(
                    $"Unable to send the verification code: {exception.Message}");
        }
    }

    public async Task<
        EmailVerificationServiceResult<VerifyEmailAddressResponse>>
        VerifyAsync(
            EmailVerificationPurpose purpose,
            int actorID,
            string emailAddress,
            string verificationID,
            string code,
            CancellationToken cancellationToken = default)
    {
        try
        {
            var response =
                await _http.PostAsJsonAsync(
                    GetApiBase(purpose, actorID) + "/verify",
                    new VerifyEmailAddressRequest
                    {
                        Purpose =
                            purpose,

                        ActorID =
                            actorID,

                        EmailAddress =
                            emailAddress,

                        VerificationID =
                            verificationID,

                        Code =
                            code
                    },
                    cancellationToken);

            return await ReadResultAsync<
                VerifyEmailAddressResponse>(
                    response,
                    cancellationToken);
        }
        catch (Exception exception)
        {
            return EmailVerificationServiceResult<
                VerifyEmailAddressResponse>.Failure(
                    $"Unable to verify the email address: {exception.Message}");
        }
    }

    private static string GetApiBase(
        EmailVerificationPurpose purpose,
        int actorID)
    {
        return purpose == EmailVerificationPurpose.NewOrganizationAdminEmail &&
               actorID == 0
            ? PublicOrganizationApiBase
            : ApiBase;
    }

    private static async Task<
        EmailVerificationServiceResult<T>>
        ReadResultAsync<T>(
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
                ? EmailVerificationServiceResult<T>.Failure(
                    "The server returned an empty response.")
                : EmailVerificationServiceResult<T>.Success(
                    value);
        }

        EmailVerificationApiError? error =
            null;

        try
        {
            error =
                await response.Content
                    .ReadFromJsonAsync<EmailVerificationApiError>(
                        cancellationToken:
                            cancellationToken);
        }
        catch
        {
            // The status-code fallback below still gives
            // the user a useful error.
        }

        return EmailVerificationServiceResult<T>.Failure(
            string.IsNullOrWhiteSpace(
                error?.Message)
                ? $"The request failed with status {(int)response.StatusCode}."
                : error.Message);
    }
}