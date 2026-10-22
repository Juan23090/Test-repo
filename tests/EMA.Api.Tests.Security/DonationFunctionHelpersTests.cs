using EMA.Api.DTOs;
using EMA.Api.Functions.Donations;

namespace EMA.Api.Tests.Security;

public sealed class DonationFunctionHelpersTests
{
    [Theory]
    [InlineData(2601)]
    [InlineData(2627)]
    public void DuplicateReferenceIndexErrorsAreRecognized(
        int errorNumber)
    {
        Assert.True(
            DonationFunctionHelpers
                .IsDuplicateReferenceNumberError(
                    errorNumber,
                    "Cannot insert duplicate key row in object 'dbo.Donation' " +
                    "with unique index 'UX_Donation_Organization_ReferenceNumber'."));
    }

    [Theory]
    [InlineData(
        2601,
        "Cannot insert duplicate key row with unique index 'UX_Other_Index'.")]
    [InlineData(
        2627,
        "Violation of UNIQUE KEY constraint 'UX_Other_Index'.")]
    [InlineData(
        515,
        "UX_Donation_Organization_ReferenceNumber")]
    public void UnrelatedSqlErrorsAreNotRecognizedAsDuplicateReferences(
        int errorNumber,
        string message)
    {
        Assert.False(
            DonationFunctionHelpers
                .IsDuplicateReferenceNumberError(
                    errorNumber,
                    message));
    }

    [Fact]
    public void ValidationNormalizesABlankReferenceNumberToNull()
    {
        var donation =
            CreateValidDonation();

        donation.ReferenceNumber =
            "   ";

        DonationFunctionHelpers.ValidateDonation(
            donation);

        Assert.Null(
            donation.ReferenceNumber);
    }

    [Fact]
    public void ValidationTrimsTheReferenceNumber()
    {
        var donation =
            CreateValidDonation();

        donation.ReferenceNumber =
            "  R-100  ";

        DonationFunctionHelpers.ValidateDonation(
            donation);

        Assert.Equal(
            "R-100",
            donation.ReferenceNumber);
    }

    [Fact]
    public void ValidationRejectsAReferenceNumberLongerThanTheDatabaseColumn()
    {
        var donation =
            CreateValidDonation();

        donation.ReferenceNumber =
            new string(
                'R',
                256);

        var exception =
            Assert.Throws<ArgumentException>(
                () => DonationFunctionHelpers
                    .ValidateDonation(
                        donation));

        Assert.Equal(
            "The reference number cannot exceed 255 characters.",
            exception.Message);
    }

    private static DonationDTO CreateValidDonation() =>
        new()
        {
            EMADonorID =
                10,

            DateTimeReceived =
                DateTime.UtcNow,

            ReferenceNumber =
                "R-100",

            DonationAmount =
                25.00m,

            Distributions =
            [
                new DonationDistributionDTO
                {
                    EMAFundID =
                        20,

                    DistributionAmount =
                        25.00m
                }
            ]
        };
}
