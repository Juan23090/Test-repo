namespace EMA.Api.Services.Authentication;

public static class EmailVerificationPurposeValidator
{
    public static bool IsEmailAddressPurpose(
        TwoFactorPurpose purpose) =>
        purpose is
            TwoFactorPurpose.NewOrganizationAdminEmail or
            TwoFactorPurpose.NewOrganizationEtransferEmail or
            TwoFactorPurpose.OrganizationAdminEmail or
            TwoFactorPurpose.OrganizationEtransferEmail or
            TwoFactorPurpose.OrganizationEmail or
            TwoFactorPurpose.OrganizationContactEmail;

    public static bool HasValidActor(
        TwoFactorPurpose purpose,
        int actorID) =>
        purpose switch
        {
            TwoFactorPurpose.NewOrganizationAdminEmail =>
                actorID == 0,

            TwoFactorPurpose.NewOrganizationEtransferEmail =>
                actorID == 0,

            TwoFactorPurpose.OrganizationAdminEmail =>
                actorID > 0,

            TwoFactorPurpose.OrganizationEtransferEmail =>
                actorID > 0,

            TwoFactorPurpose.OrganizationEmail =>
                actorID > 0,

            TwoFactorPurpose.OrganizationContactEmail =>
                actorID > 0,

            _ =>
                false
        };
}