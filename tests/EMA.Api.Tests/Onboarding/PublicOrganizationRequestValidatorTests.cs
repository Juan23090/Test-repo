using EMA.Api.DTOs;
using EMA.Api.Services.Onboarding;

namespace EMA.Api.Tests.Onboarding;

public sealed class PublicOrganizationRequestValidatorTests
{
    [Theory]
    [InlineData(10)]
    [InlineData(30)]
    [InlineData(40)]
    [InlineData(50)]
    public void AllowedTask26StatusesAreAccepted(int statusID)
    {
        Assert.True(PublicOrganizationRequestValidator.IsAllowedStatus(statusID));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    [InlineData(25)]
    [InlineData(1000)]
    public void OtherStatusesAreRejected(int statusID)
    {
        Assert.False(PublicOrganizationRequestValidator.IsAllowedStatus(statusID));
    }

    [Fact]
    public void CompleteRequestPassesValidation()
    {
        Assert.Null(PublicOrganizationRequestValidator.Validate(ValidRequest()));
    }

    [Fact]
    public void MissingVerificationTokenIsRejected()
    {
        var request = ValidRequest();
        request.AdminEmailVerificationToken = null;

        var message = PublicOrganizationRequestValidator.Validate(request);

        Assert.NotNull(message);
        Assert.Contains("verified", message!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidProvinceIsRejected()
    {
        var request = ValidRequest();
        request.ProvinceStateCode = "not-a-province";

        var message = PublicOrganizationRequestValidator.Validate(request);

        Assert.NotNull(message);
        Assert.Contains("province", message!, StringComparison.OrdinalIgnoreCase);
    }

    private static PublicOrganizationRequestDto ValidRequest() =>
        new()
        {
            OrganizationName = "Example Organization",
            AdminName = "Example Admin",
            AdminEmail = "admin@example.org",
            AdminEmailVerificationToken = "verification-id",
            AdminPhone = "3065550100",
            Address = "1 Main Street",
            City = "Saskatoon",
            ProvinceStateCode = "SK",
            PostalCode = "S7K 0J5",
            AnnualBudget = "50k-100k",
            ChMSID = 1,
            AccountingSoftwareID = 1,
            OrganizationStatusID = 40
        };
}
