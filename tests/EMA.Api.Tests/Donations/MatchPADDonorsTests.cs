using EMA.Api.Functions.Donations;
using Xunit;

namespace EMA.Api.Tests.Donations;

public class MatchPADDonorsTests
{
    // Verifies exact name match sets status to Auto-Matched with single candidate ID
    [Fact]
    public void EvaluateLineMatching_ExactNameMatch_ReturnsAutoMatched()
    {
        var line = new PADLineRecord { PADBatchLineID = 1, PADDonorName = "Christian Bale & Matt Damon" };
        var donors = new List<DonorRecord>
        {
            new() { EMADonorID = 101, OfficialDonorName = "Christian Bale & Matt Damon" },
            new() { EMADonorID = 102, OfficialDonorName = "Lionel Messi" }
        };

        var result = MatchPADDonors.EvaluateLineMatching(line, donors);

        Assert.Equal("Auto-Matched", result.MatchStatus);
        Assert.True(result.IsAutoMatched);
        Assert.False(result.IsAmbiguous);
        Assert.Equal(101, result.MatchedDonorID);
        Assert.Equal("Christian Bale & Matt Damon", result.MatchedDonorName);
    }

    // Verifies token matching correctly pairs names regardless of word ordering
    [Fact]
    public void EvaluateLineMatching_ReorderedTokens_MatchesHighestTokenOverlap()
    {
        var line = new PADLineRecord { PADBatchLineID = 2, PADDonorName = "Damon Matt Bale Christian" };
        var donors = new List<DonorRecord>
        {
            new() { EMADonorID = 201, OfficialDonorName = "Christian Bale & Matt Damon" },
            new() { EMADonorID = 202, OfficialDonorName = "Leonardo DiCaprio" }
        };

        var result = MatchPADDonors.EvaluateLineMatching(line, donors);

        Assert.Equal("Auto-Matched", result.MatchStatus);
        Assert.Equal(201, result.MatchedDonorID);
    }

    // Verifies ties between multiple matching donors trigger ambiguous status and returns candidate list
    [Fact]
    public void EvaluateLineMatching_ScoreTie_ReturnsAmbiguousWithCandidates()
    {
        var line = new PADLineRecord { PADBatchLineID = 3, PADDonorName = "Michael Williams" };
        var donors = new List<DonorRecord>
        {
            new() { EMADonorID = 301, OfficialDonorName = "Michael Jordan", Email = "mj@example.com" },
            new() { EMADonorID = 302, OfficialDonorName = "Michael Jackson", Email = "jackson@example.com" },
            new() { EMADonorID = 303, OfficialDonorName = "Lionel Messi", Email = "messi@example.com" }
        };

        var result = MatchPADDonors.EvaluateLineMatching(line, donors);

        Assert.Equal("Ambiguous (Ties)", result.MatchStatus);
        Assert.True(result.IsAmbiguous);
        Assert.False(result.IsAutoMatched);
        Assert.Equal(2, result.CandidateDonors.Count);
        Assert.Contains(result.CandidateDonors, c => c.EMADonorID == 301);
        Assert.Contains(result.CandidateDonors, c => c.EMADonorID == 302);
    }

    // Verifies non-matching statement names return No Match status
    [Fact]
    public void EvaluateLineMatching_NoMatchingTokens_ReturnsNoMatch()
    {
        var line = new PADLineRecord { PADBatchLineID = 4, PADDonorName = "Unknown Statement Donor" };
        var donors = new List<DonorRecord>
        {
            new() { EMADonorID = 401, OfficialDonorName = "Christian Bale" },
            new() { EMADonorID = 402, OfficialDonorName = "Scarlett Johansson" }
        };

        var result = MatchPADDonors.EvaluateLineMatching(line, donors);

        Assert.Equal("No Match", result.MatchStatus);
        Assert.False(result.IsAutoMatched);
        Assert.False(result.IsAmbiguous);
        Assert.Null(result.MatchedDonorID);
    }

    // Verifies pre-assigned official donor names in the line record are preserved as Selected
    [Fact]
    public void EvaluateLineMatching_PreExistingOfficialDonor_PreservesSelectedStatus()
    {
        var line = new PADLineRecord 
        { 
            PADBatchLineID = 5, 
            PADDonorName = "Johansson Scarlett", 
            OfficialDonorName = "Scarlett Johansson" 
        };
        var donors = new List<DonorRecord>
        {
            new() { EMADonorID = 501, OfficialDonorName = "Scarlett Johansson" },
            new() { EMADonorID = 502, OfficialDonorName = "Jennifer Lawrence" }
        };

        var result = MatchPADDonors.EvaluateLineMatching(line, donors);

        Assert.Equal("Selected", result.MatchStatus);
        Assert.True(result.IsAutoMatched);
        Assert.False(result.IsAmbiguous);
        Assert.Equal(501, result.MatchedDonorID);
        Assert.Equal("Scarlett Johansson", result.MatchedDonorName);
    }

    // Verifies tokenizer strips punctuation and ignores single character tokens except ampersand
    [Fact]
    public void TokenizeName_PunctuationAndConjunctions_ReturnsCleanTokenSet()
    {
        string rawInput = "James, LeBron & Michael Jordan (PAD)";

        var tokens = MatchPADDonors.TokenizeName(rawInput);

        Assert.Contains("James", tokens);
        Assert.Contains("LeBron", tokens);
        Assert.Contains("&", tokens);
        Assert.Contains("Michael", tokens);
        Assert.Contains("Jordan", tokens);
        Assert.Contains("PAD", tokens);
    }
}
