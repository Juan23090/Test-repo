namespace EMA.Api.DTOs;

public sealed class SandboxWorkspaceSummaryDto
{
    public int SandboxOrganizationID { get; set; }

    public int SandboxUserID { get; set; }

    public string OrganizationName { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    public string SandboxEmailAddress { get; set; } = string.Empty;

    public DateTime? DateTimeCreated { get; set; }

    public DateTime? LastDonationDate { get; set; }
}

public sealed class CurrentSandboxResponseDto
{
    public bool HasActiveSandbox { get; set; }

    public SandboxWorkspaceSummaryDto? Workspace { get; set; }
}

public sealed class CreateSandboxResponseDto
{
    public SandboxWorkspaceSummaryDto Workspace { get; set; } = new();

    public string Capability { get; set; } = string.Empty;
}

public sealed class ResumeSandboxRequestDto
{
    public int SandboxOrganizationID { get; set; }
}

public sealed class ResumeSandboxByPhoneRequestDto
{
    public string PhoneNumber { get; set; } = string.Empty;
}

public sealed class SandboxTestEmailDraftDto
{
    public int SandboxOrganizationID { get; set; }

    public string SandboxEmailAddress { get; set; } = string.Empty;

    public string SenderName { get; set; } = "Some Guy";

    public string SenderEmailAddress { get; set; } = "test.donor@example.com";

    public decimal Amount { get; set; } = 100.00m;

    public string ReferenceNumber { get; set; } = "C1test1234";

    public string Message { get; set; } = "Testing Email Money App";
}

public sealed class SendSandboxTestEmailRequestDto
{
    public int SandboxOrganizationID { get; set; }

    public string SenderName { get; set; } = string.Empty;

    public string SenderEmailAddress { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string ReferenceNumber { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}

public sealed class SendSandboxTestEmailResponseDto
{
    public string Message { get; set; } = string.Empty;
}

public sealed class SandboxCleanupResultDto
{
    public int DeletedSandboxCount { get; set; }

    public List<string> RemovedSandboxEmailAddresses { get; set; } = new();
}
