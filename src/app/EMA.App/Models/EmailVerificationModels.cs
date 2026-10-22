namespace EMA.App.Models;

public enum EmailVerificationPurpose
{
    NewOrganizationAdminEmail = 10,
    OrganizationAdminEmail = 11,
    OrganizationEtransferEmail = 12,
    NewOrganizationEtransferEmail = 13,

    OrganizationEmail = 14,
    OrganizationContactEmail = 15
}

public sealed class StartEmailVerificationRequest
{
    public EmailVerificationPurpose Purpose { get; set; }

    public int ActorID { get; set; }

    public string EmailAddress { get; set; } =
        string.Empty;
}

public sealed class StartEmailVerificationResponse
{
    public string VerificationID { get; set; } =
        string.Empty;

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public string Message { get; set; } =
        string.Empty;
}

public sealed class VerifyEmailAddressRequest
{
    public EmailVerificationPurpose Purpose { get; set; }

    public int ActorID { get; set; }

    public string EmailAddress { get; set; } =
        string.Empty;

    public string VerificationID { get; set; } =
        string.Empty;

    public string Code { get; set; } =
        string.Empty;
}

public sealed class VerifyEmailAddressResponse
{
    public string VerificationToken { get; set; } =
        string.Empty;

    public string Message { get; set; } =
        string.Empty;
}

public sealed class EmailVerificationCompleted
{
    public required string EmailAddress { get; init; }

    public required string VerificationToken { get; init; }
}

public sealed class EmailVerificationServiceResult<T>
{
    public bool IsSuccess { get; private init; }

    public T? Value { get; private init; }

    public string ErrorMessage { get; private init; } =
        string.Empty;

    public static EmailVerificationServiceResult<T> Success(
        T value) =>
        new()
        {
            IsSuccess = true,
            Value = value
        };

    public static EmailVerificationServiceResult<T> Failure(
        string message) =>
        new()
        {
            IsSuccess = false,
            ErrorMessage = message
        };
}

public sealed class EmailVerificationApiError
{
    public string Message { get; set; } =
        string.Empty;
}