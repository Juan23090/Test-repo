using EMA.Api.DTOs;
using EMA.Api.Services.Authentication;

namespace EMA.Api.Services.Onboarding;

public static class PublicOrganizationRequestValidator
{
    private static readonly HashSet<int> AllowedStatusIDs =
    [
        10,
        30,
        40,
        50
    ];

    public static bool IsAllowedStatus(int? statusID) =>
        statusID.HasValue && AllowedStatusIDs.Contains(statusID.Value);

    public static string? Validate(PublicOrganizationRequestDto? request)
    {
        if (request is null)
        {
            return "Organization information is required.";
        }

        if (!IsAllowedStatus(request.OrganizationStatusID))
        {
            return "Please choose a valid onboarding option.";
        }

        if (string.IsNullOrWhiteSpace(request.OrganizationName) ||
            string.IsNullOrWhiteSpace(request.AdminName) ||
            string.IsNullOrWhiteSpace(request.AdminEmail) ||
            string.IsNullOrWhiteSpace(request.AdminEmailVerificationToken) ||
            string.IsNullOrWhiteSpace(request.AdminPhone) ||
            string.IsNullOrWhiteSpace(request.Address) ||
            string.IsNullOrWhiteSpace(request.City) ||
            string.IsNullOrWhiteSpace(request.ProvinceStateCode) ||
            string.IsNullOrWhiteSpace(request.PostalCode) ||
            string.IsNullOrWhiteSpace(request.AnnualBudget) ||
            request.ChMSID is null or <= 0 ||
            request.AccountingSoftwareID is null or <= 0)
        {
            return "All fields are required and the contact email must be verified.";
        }

        if (request.OrganizationName.Trim().Length > 255 ||
            request.AdminName.Trim().Length > 255 ||
            request.AdminPhone.Trim().Length > 255 ||
            request.Address.Trim().Length > 255 ||
            request.City.Trim().Length > 255 ||
            request.PostalCode.Trim().Length > 255 ||
            request.AnnualBudget.Trim().Length > 255)
        {
            return "One or more fields exceed the maximum allowed length.";
        }

        try
        {
            _ = EmailAddressNormalizer.Normalize(request.AdminEmail);
        }
        catch (ArgumentException exception)
        {
            return exception.Message;
        }

        var provinceStateCode =
            ProvinceStateCodeNormalizer.Normalize(request.ProvinceStateCode);

        if (provinceStateCode is null)
        {
            return "Please enter a valid Canadian province or U.S. state using its name or 2-letter code.";
        }

        return null;
    }
}
