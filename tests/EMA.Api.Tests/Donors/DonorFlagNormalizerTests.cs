using System.Data;
using EMA.Api.Services.Donors;

namespace EMA.Api.Tests.Donors;

/*
    These donor flags drive email-ingestion routing, so the create-versus-update
    asymmetry is the behaviour under test: create falls back to the column
    default, update preserves whatever is already stored.
*/
public class DonorFlagNormalizerTests
{
    [Theory]
    [InlineData("Y", "Y")]
    [InlineData("N", "N")]
    [InlineData("y", "Y")]
    [InlineData("n", "N")]
    [InlineData(" Y ", "Y")]
    public void RecognizedValuesRoundTripOnUpdate(
        string input,
        string expected)
    {
        Assert.Equal(
            expected,
            DonorFlagNormalizer.NormalizeForUpdate(input));
    }

    /*
        The blocker this class exists to prevent: an older or partial caller
        that omits the field, or sends something unexpected, must not reset
        the flag. DBNull tells the stored procedure to keep the stored value.
    */
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Yes")]
    [InlineData("No")]
    [InlineData("true")]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("X")]
    public void UnrecognizedValuesPreserveTheStoredValueOnUpdate(
        string? input)
    {
        Assert.Equal(
            DBNull.Value,
            DonorFlagNormalizer.NormalizeForUpdate(input));
    }

    [Theory]
    [InlineData("Y", "Y")]
    [InlineData("N", "N")]
    [InlineData("y", "Y")]
    [InlineData("n", "N")]
    public void RecognizedValuesRoundTripOnCreate(
        string input,
        string expected)
    {
        Assert.Equal(
            expected,
            DonorFlagNormalizer.NormalizeForCreate(
                input,
                DonorFlagNormalizer.No));

        Assert.Equal(
            expected,
            DonorFlagNormalizer.NormalizeForCreate(
                input,
                DonorFlagNormalizer.Yes));
    }

    /*
        On insert there is no prior value, so the column default is correct.
        Both fallbacks are exercised because the pilot flag defaults to
        'N', while both acknowledgement preferences default to 'Y'.
    */
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Yes")]
    [InlineData("X")]
    public void UnrecognizedValuesFallBackToTheColumnDefaultOnCreate(
        string? input)
    {
        Assert.Equal(
            DonorFlagNormalizer.No,
            DonorFlagNormalizer.NormalizeForCreate(
                input,
                DonorFlagNormalizer.No));

        Assert.Equal(
            DonorFlagNormalizer.Yes,
            DonorFlagNormalizer.NormalizeForCreate(
                input,
                DonorFlagNormalizer.Yes));
    }

    /*
        The insert columns are NOT NULL and carry a CHECK ('Y','N'), so create
        must always produce one of those two - never null, empty, or a
        passed-through value. The string return type rules out DBNull at
        compile time; this covers the rest.
    */
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    public void CreateAlwaysYieldsAValueTheCheckConstraintAccepts(
        string? input)
    {
        var result =
            DonorFlagNormalizer.NormalizeForCreate(
                input,
                DonorFlagNormalizer.No);

        Assert.Contains(
            result,
            new[]
            {
                DonorFlagNormalizer.Yes,
                DonorFlagNormalizer.No
            });
    }

    [Theory]
    [InlineData("")]
    [InlineData("Q")]
    [InlineData("Yes")]
    public void AnInvalidFallbackIsRejected(
        string fallback)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DonorFlagNormalizer.NormalizeForCreate(
                "unrecognized",
                fallback));
    }
}
