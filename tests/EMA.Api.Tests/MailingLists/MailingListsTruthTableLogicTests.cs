namespace EMA.Api.Tests.MailingLists;

public sealed class MailingListsTruthTableLogicTests
{
    [Theory]
    // Selected, OptedIn, OptedOut -> Expected MailingList Result
    [InlineData(true, false, false, true)]   // Selected based on giving history -> Yes
    [InlineData(true, true, false, true)]    // Selected AND Opted In -> Yes
    [InlineData(false, true, false, true)]   // Not selected, but Opted In -> Yes (Opt-in overrides selection)
    [InlineData(true, false, true, false)]   // Selected, but Opted Out -> No (Opt-out overrides selection)
    [InlineData(true, true, true, false)]    // Selected, Opted In, but also Opted Out -> No (Opt-out overrides EVERYTHING)
    [InlineData(false, true, true, false)]   // Not selected, Opted In, but also Opted Out -> No (Opt-out overrides opt-in)
    [InlineData(false, false, true, false)]  // Not selected, Not opted in, Opted Out -> No
    [InlineData(false, false, false, false)] // Not selected, Not opted in, Not opted out -> No
    public void TruthTableLogic_CalculatesSendListEligibilityCorrectly(
        bool selected, 
        bool optedIn, 
        bool optedOut, 
        bool expectedInSendList)
    {
        // Core business rule: Opt-out overrides everything; otherwise, Opt-in or Selected includes the donor
        bool actual = !optedOut && (optedIn || selected);
        Assert.Equal(expectedInSendList, actual);
    }

    [Theory]
    [InlineData("Email", "john@example.com", "123 Main St", true)]
    [InlineData("Email", "", "123 Main St", false)]
    [InlineData("Email", null, "123 Main St", false)]
    [InlineData("Postal Mail", "john@example.com", "123 Main St", true)]
    [InlineData("Postal Mail", "john@example.com", "", false)]
    [InlineData("Postal Mail", "john@example.com", null, false)]
    public void SendMethodRequirements_ValidatesDonorDeliveryAddress(
        string sendMethod,
        string? email,
        string? address,
        bool expectedValid)
    {
        bool isValid = sendMethod switch
        {
            "Email" => !string.IsNullOrWhiteSpace(email),
            "Postal Mail" => !string.IsNullOrWhiteSpace(address),
            _ => true
        };

        Assert.Equal(expectedValid, isValid);
    }

    [Theory]
    [InlineData(null, null, null, null, false)] // All null -> not active
    [InlineData("2024-01-01", null, null, null, true)] // Only StartDate
    [InlineData(null, "2024-12-31", null, null, true)] // Only EndDate
    [InlineData(null, null, 50.00, null, true)] // Only MoneyFrom
    [InlineData(null, null, null, 500.00, true)] // Only MoneyTo
    [InlineData(null, "2024-12-31", null, 250.00, true)] // EndDate and MoneyTo
    [InlineData("2024-01-01", "2024-12-31", 50.00, 500.00, true)] // All configured
    public void Criteria2Presence_DeterminesIfCriteria2ParticipatesInAndEvaluation(
        string? startDateStr,
        string? endDateStr,
        double? moneyFromDouble,
        double? moneyToDouble,
        bool expectedIsActive)
    {
        DateTime? startDate = startDateStr is not null ? DateTime.Parse(startDateStr) : null;
        DateTime? endDate = endDateStr is not null ? DateTime.Parse(endDateStr) : null;
        decimal? moneyFrom = moneyFromDouble.HasValue ? (decimal)moneyFromDouble.Value : null;
        decimal? moneyTo = moneyToDouble.HasValue ? (decimal)moneyToDouble.Value : null;

        bool isActive = startDate is not null
            || endDate is not null
            || moneyFrom is not null
            || moneyTo is not null;

        Assert.Equal(expectedIsActive, isActive);
    }

    [Theory]
    // Filter by only EndDate (<= 2024-06-30)
    [InlineData(null, "2024-06-30", null, null, "2024-05-01", 100.0, true)]
    [InlineData(null, "2024-06-30", null, null, "2024-07-01", 100.0, false)]
    // Filter by only MoneyTo (<= 100.00)
    [InlineData(null, null, null, 100.0, "2024-05-01", 50.0, true)]
    [InlineData(null, null, null, 100.0, "2024-05-01", 150.0, false)]
    // Filter by EndDate AND MoneyTo (<= 2024-06-30 and <= 100.00)
    [InlineData(null, "2024-06-30", null, 100.0, "2024-05-01", 75.0, true)]
    [InlineData(null, "2024-06-30", null, 100.0, "2024-07-01", 75.0, false)]
    [InlineData(null, "2024-06-30", null, 100.0, "2024-05-01", 150.0, false)]
    public void CriteriaEvaluation_MatchesDonationFilters(
        string? startDateStr,
        string? endDateStr,
        double? moneyFromDouble,
        double? moneyToDouble,
        string donationDateStr,
        double donationAmountDouble,
        bool expectedMatch)
    {
        DateTime? startDate = startDateStr is not null ? DateTime.Parse(startDateStr) : null;
        DateTime? endDate = endDateStr is not null ? DateTime.Parse(endDateStr) : null;
        decimal? moneyFrom = moneyFromDouble.HasValue ? (decimal)moneyFromDouble.Value : null;
        decimal? moneyTo = moneyToDouble.HasValue ? (decimal)moneyToDouble.Value : null;

        DateTime donationDate = DateTime.Parse(donationDateStr);
        decimal donationAmount = (decimal)donationAmountDouble;

        bool matches = (startDate is null || donationDate >= startDate)
            && (endDate is null || donationDate <= endDate)
            && (moneyFrom is null || donationAmount >= moneyFrom)
            && (moneyTo is null || donationAmount <= moneyTo);

        Assert.Equal(expectedMatch, matches);
    }
}

