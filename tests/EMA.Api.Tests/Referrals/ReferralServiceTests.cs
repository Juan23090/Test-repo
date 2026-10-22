using System.Text.Json;
using EMA.Api.Models;
using EMA.Api.Services.Email;
using EMA.Api.Services.Referrals;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EMA.Api.Tests.Referrals;

public sealed class ReferralServiceTests
{
    [Fact]
    public void ReferralEligibilityDto_SerializesAndDeserializesCorrectly()
    {
        var dto = new ReferralEligibilityDto
        {
            IsEligible = true,
            Reason = "Active for 14+ days with donations",
            DaysActive = 30,
            DonationCount = 12,
            DefaultPreface = "Hello friend",
            EmailBody = "EMA is great."
        };

        var json = JsonSerializer.Serialize(dto);
        var deserialized = JsonSerializer.Deserialize<ReferralEligibilityDto>(json);

        Assert.NotNull(deserialized);
        Assert.True(deserialized!.IsEligible);
        Assert.Equal(30, deserialized.DaysActive);
        Assert.Equal(12, deserialized.DonationCount);
        Assert.Equal("Hello friend", deserialized.DefaultPreface);
    }

    [Fact]
    public void SendReferralsRequest_JsonSerialization_PreservesRecipients()
    {
        var request = new SendReferralsRequest
        {
            PrefaceText = "Please take a look at EMA.",
            Recipients = new List<SendReferralRecipientDto>
            {
                new()
                {
                    OrganizationName = "Hope Outreach",
                    ContactName = "Pastor Bob",
                    EmailAddress = "bob@hopeoutreach.ca"
                },
                new()
                {
                    OrganizationName = "Grace Foundation",
                    ContactName = "Mary Smith",
                    EmailAddress = "mary@grace.org"
                }
            }
        };

        var json = JsonSerializer.Serialize(request);
        var deserialized = JsonSerializer.Deserialize<SendReferralsRequest>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized!.Recipients.Count);
        Assert.Equal("bob@hopeoutreach.ca", deserialized.Recipients[0].EmailAddress);
        Assert.Equal("Grace Foundation", deserialized.Recipients[1].OrganizationName);
    }

    [Fact]
    public void AcceptReferralResult_SetsSuccessCorrectly()
    {
        var successResult = new AcceptReferralResult
        {
            IsSuccess = true,
            Message = "Referral claimed successfully.",
            ReferralID = 101,
            ReferringOrgID = 5,
            ReferringOrgName = "St. Andrew's Community",
            Status = "SUCCESS"
        };

        var failureResult = new AcceptReferralResult
        {
            IsSuccess = false,
            Message = "This referral has already been claimed.",
            Status = "ALREADY_CLAIMED"
        };

        Assert.True(successResult.IsSuccess);
        Assert.Equal(101, successResult.ReferralID);
        Assert.False(failureResult.IsSuccess);
        Assert.Equal("ALREADY_CLAIMED", failureResult.Status);
    }

    [Fact]
    public void ReferralSummaryDto_CalculatesTotalsProperly()
    {
        var summary = new ReferralSummaryDto
        {
            TotalSent = 3,
            TotalAccepted = 2,
            TotalCredited = 1,
            AccumulatedCreditBalance = 45.00m,
            History = new List<ReferralHistoryItemDto>
            {
                new()
                {
                    ReferralID = 1,
                    RecipientOrganization = "Org A",
                    Status = "Credited",
                    DateReferred = DateTime.UtcNow.AddDays(-30),
                    DateAccepted = DateTime.UtcNow.AddDays(-20),
                    DateCredited = DateTime.UtcNow.AddDays(-5),
                    CreditAmount = 45.00m
                },
                new()
                {
                    ReferralID = 2,
                    RecipientOrganization = "Org B",
                    Status = "Accepted",
                    DateReferred = DateTime.UtcNow.AddDays(-10),
                    DateAccepted = DateTime.UtcNow.AddDays(-2)
                },
                new()
                {
                    ReferralID = 3,
                    RecipientOrganization = "Org C",
                    Status = "Pending",
                    DateReferred = DateTime.UtcNow.AddDays(-1)
                }
            }
        };

        Assert.Equal(3, summary.TotalSent);
        Assert.Equal(2, summary.TotalAccepted);
        Assert.Equal(1, summary.TotalCredited);
        Assert.Equal(45.00m, summary.AccumulatedCreditBalance);
        Assert.Equal(3, summary.History.Count);
    }

    [Fact]
    public async Task SendBatchReferrals_WithEmptyRecipients_ReturnsEmptyResultImmediately()
    {
        var mockEmail = new Mock<IEmailService>();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SqlConnectionString"] = "Server=(local);Database=ema;Integrated Security=true;",
                ["ACS_EMAIL_SENDER"] = "test@emailmoney.app"
            })
            .Build();

        var service = new ReferralService(config, mockEmail.Object, NullLogger<ReferralService>.Instance);

        var result = await service.SendBatchReferralsAsync(1, 1, new SendReferralsRequest { Recipients = new() });

        Assert.Equal(0, result.TotalRequested);
        Assert.Equal(0, result.SentCount);
        Assert.Equal(0, result.FailedCount);
    }

    [Fact]
    public async Task AcceptReferral_WithEmptyCode_ReturnsFailedValidation()
    {
        var mockEmail = new Mock<IEmailService>();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SqlConnectionString"] = "Server=(local);Database=ema;Integrated Security=true;",
                ["ACS_EMAIL_SENDER"] = "test@emailmoney.app"
            })
            .Build();

        var service = new ReferralService(config, mockEmail.Object, NullLogger<ReferralService>.Instance);

        var result = await service.AcceptReferralAsync(2, 1, "", "valid@email.com");

        Assert.False(result.IsSuccess);
        Assert.Contains("valid referral code", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AcceptReferral_WithInvalidEmail_ReturnsFailedValidation()
    {
        var mockEmail = new Mock<IEmailService>();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SqlConnectionString"] = "Server=(local);Database=ema;Integrated Security=true;",
                ["ACS_EMAIL_SENDER"] = "test@emailmoney.app"
            })
            .Build();

        var service = new ReferralService(config, mockEmail.Object, NullLogger<ReferralService>.Instance);

        var result = await service.AcceptReferralAsync(2, 1, "test-code", "not-an-email");

        Assert.False(result.IsSuccess);
        Assert.Contains("valid referral email", result.Message, StringComparison.OrdinalIgnoreCase);
    }
}
