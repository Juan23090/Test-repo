namespace EMA.App.Models;

public sealed class SandboxWorkspaceSummary
{
    public int SandboxOrganizationID { get; set; }

    public int SandboxUserID { get; set; }

    public string OrganizationName { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    public string SandboxEmailAddress { get; set; } = string.Empty;

    public DateTime? DateTimeCreated { get; set; }

    public DateTime? LastDonationDate { get; set; }
}

public sealed class CurrentSandboxResponse
{
    public bool HasActiveSandbox { get; set; }

    public SandboxWorkspaceSummary? Workspace { get; set; }
}

public sealed class CreateSandboxResponse
{
    public SandboxWorkspaceSummary Workspace { get; set; } = new();

    public string Capability { get; set; } = string.Empty;
}

public sealed class ResumeSandboxRequest
{
    public int SandboxOrganizationID { get; set; }
}

public sealed class SandboxTestEmailDraft
{
    public int SandboxOrganizationID { get; set; }

    public string SandboxEmailAddress { get; set; } = string.Empty;

    public string SenderName { get; set; } = "Some Guy";

    public string SenderEmailAddress { get; set; } = "test.donor@example.com";

    public decimal Amount { get; set; } = 100.00m;

    public string ReferenceNumber { get; set; } = "C1test1234";

    public string Message { get; set; } = "Testing Email Money App";
}

public sealed class SendSandboxTestEmailRequest
{
    public int SandboxOrganizationID { get; set; }

    public string SenderName { get; set; } = string.Empty;

    public string SenderEmailAddress { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string ReferenceNumber { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}

public sealed class SendSandboxTestEmailResponse
{
    public string Message { get; set; } = string.Empty;
}

public sealed class SandboxApiError
{
    public string Message { get; set; } = string.Empty;
}

public sealed class SandboxServiceResult<T>
{
    private SandboxServiceResult(
        bool isSuccess,
        T? value,
        string? errorMessage,
        int? statusCode)
    {
        IsSuccess = isSuccess;
        Value = value;
        ErrorMessage = errorMessage;
        StatusCode = statusCode;
    }

    public bool IsSuccess { get; }

    public T? Value { get; }

    public string? ErrorMessage { get; }

    public int? StatusCode { get; }

    public static SandboxServiceResult<T> Success(
        T value,
        int? statusCode = null)
    {
        return new SandboxServiceResult<T>(
            true,
            value,
            null,
            statusCode);
    }

    public static SandboxServiceResult<T> Failure(
        string errorMessage,
        int? statusCode = null)
    {
        return new SandboxServiceResult<T>(
            false,
            default,
            errorMessage,
            statusCode);
    }
}
