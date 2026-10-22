namespace EMA.Api.Services.Authentication;

public enum TwoFactorPurpose
{
    OrganizationLogin = 1,
    DonorLogin = 2,
    UnifiedLogin = 3,

    NewOrganizationAdminEmail = 10,
    OrganizationAdminEmail = 11,
    OrganizationEtransferEmail = 12,
    NewOrganizationEtransferEmail = 13,

    OrganizationEmail = 14,
    OrganizationContactEmail = 15
}
