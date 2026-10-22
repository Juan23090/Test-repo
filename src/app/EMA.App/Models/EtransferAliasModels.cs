namespace EMA.App.Models;

public sealed class EtransferAliasAvailabilityResult
{
    public bool CheckSucceeded { get; set; } = true;
    public bool IsAvailable { get; set; }
    public bool IsCurrentOrganizationAlias { get; set; }
    public string Prefix { get; set; } = string.Empty;
    public string EmailAddress { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class OrganizationUpdateResult
{
    public bool Succeeded { get; set; }
    public int StatusCode { get; set; }
    public string Message { get; set; } = string.Empty;
}