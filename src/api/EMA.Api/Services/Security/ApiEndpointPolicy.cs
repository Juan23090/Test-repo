namespace EMA.Api.Services.Security;

public enum ApiEndpointAccess
{
    Public = 1,
    SelfContainedDonorToken = 2,
    OrganizationUser = 3,
    Donor = 4,
    PlatformAdministrator = 5,
    Authenticated = 6,
    Disabled = 7,
    OrganizationAdministrator = 8
}

public sealed record ApiEndpointPolicy(
    ApiEndpointAccess Access,
    long MaximumRequestBodyBytes = 1_048_576,
    bool AllowPlatformCrossTenant = false);
