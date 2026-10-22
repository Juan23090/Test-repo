using EMA.App.Services;
using Xunit;

namespace EMA.App.Tests.Donations;

public class PADLineParserTests
{
    // Verifies date and amount parsing across various calendar formats
    [Theory]
    [InlineData("Jul 01, 2026 EFT Christian Bale & Matt Damon $200.00", 2026, 7, 1, 200.00, "Christian Bale & Matt Damon")]
    [InlineData("2026-07-15 PAD Settlement Michael Jordan 50.00", 2026, 7, 15, 50.00, "Michael Jordan")]
    [InlineData("07/20/2026 Direct Debit Jennifer Lawrence $1,250.50", 2026, 7, 20, 1250.50, "Jennifer Lawrence")]
    public void ParseLine_VariousDateAndAmountFormats_ExtractsValuesCorrectly(
        string rawLine, int year, int month, int day, decimal expectedAmount, string expectedDonor)
    {
        var result = PADLineParser.ParseLine(rawLine);

        Assert.True(result.IsValid);
        Assert.Equal(new DateTime(year, month, day), result.Date);
        Assert.Equal(expectedAmount, result.Amount);
        Assert.Equal(expectedDonor, result.DonorName);
    }

    // Verifies bank transaction keywords are stripped from the extracted donor name
    [Theory]
    [InlineData("Jul 01, 2026 EFT SETTLEMENT Lionel Messi $75.00", "Lionel Messi")]
    [InlineData("Jul 01, 2026 PRE-AUTH DEBIT LeBron James $100.00", "Lebron James")]
    [InlineData("Jul 01, 2026 DIRECT DEBIT Scarlett Johansson $150.00", "Scarlett Johansson")]
    public void ParseLine_BankingKeywords_StripsNoiseFromDonorName(string rawLine, string expectedName)
    {
        var result = PADLineParser.ParseLine(rawLine);

        Assert.Equal(expectedName, result.DonorName);
    }

    // Verifies non-transaction headers and disclaimers are marked invalid
    [Theory]
    [InlineData("Transaction History for Account 12345")]
    [InlineData("Date Description Debit Credit Running Balance")]
    [InlineData("Page 1 of 5")]
    [InlineData("Online Banking Disclaimer - Generated July 2026")]
    public void ParseLine_HeaderAndDisclaimerLines_ReturnsInvalid(string rawLine)
    {
        var result = PADLineParser.ParseLine(rawLine);

        Assert.False(result.IsValid);
    }

    // Verifies donor names with ampersands and multiple words are preserved
    [Fact]
    public void ParseLine_CompoundNames_PreservesFullString()
    {
        string rawLine = "Jul 01, 2026 EFT LeBron James & Michael Jordan $100.00";

        var result = PADLineParser.ParseLine(rawLine);

        Assert.Equal("Lebron James & Michael Jordan", result.DonorName);
    }

    // Verifies RBC CSV format extracts date, amount, donor, and eTransfer type
    [Fact]
    public void TryParseCsvRow_RbcRow_ExtractsDateAmountNameAndEtransferType()
    {
        string rbcCsvLine = "Chequing,02979-1087089,7/27/2026,,e-Transfer - Autodeposit,LEBRON JAMES C1AmGJczgP4E,50,";
        var keyphrases = new List<BankKeyPhraseDto>
        {
            new() { KeyPhrase = "e-Transfer - Autodeposit", TransactionTypeID = "eTransfer" }
        };

        var result = PADLineParser.TryParseCsvRow(rbcCsvLine, keyphrases);

        Assert.NotNull(result);
        Assert.True(result.IsValid);
        Assert.Equal(new DateTime(2026, 7, 27), result.Date);
        Assert.Equal(50.00m, result.Amount);
        Assert.Equal("Lebron James", result.DonorName);
        Assert.Equal("eTransfer", result.TransactionType);
    }

    // Verifies ATB CSV format extracts date, amount, donor, and PAD type
    [Fact]
    public void TryParseCsvRow_AtbRow_ExtractsDateAmountNameAndPadType()
    {
        string atbCsvLine = "08/20/2026,21907669,0000000114158924,EFT,,,25.00,54319.78,EFT Settlement ROBERT DOWNEY JR,,";
        var keyphrases = new List<BankKeyPhraseDto>
        {
            new() { KeyPhrase = "EFT Settlement", TransactionTypeID = "PAD" }
        };

        var result = PADLineParser.TryParseCsvRow(atbCsvLine, keyphrases);

        Assert.NotNull(result);
        Assert.True(result.IsValid);
        Assert.Equal(new DateTime(2026, 8, 20), result.Date);
        Assert.Equal(25.00m, result.Amount);
        Assert.Equal("Robert Downey Jr", result.DonorName);
        Assert.Equal("PAD", result.TransactionType);
    }

    // Verifies bank fee rows are kept with empty transaction type for full ingestion
    [Fact]
    public void TryParseCsvRow_BankFeeRow_RetainsRowWithEmptyTransactionType()
    {
        string feeCsvLine = "07/31/2026,21907669,0000000114158924,Fee,,16.20,,50891.78,Fee Service,,";

        var result = PADLineParser.TryParseCsvRow(feeCsvLine, null);

        Assert.NotNull(result);
        Assert.True(result.IsValid);
        Assert.Equal(new DateTime(2026, 7, 31), result.Date);
        Assert.Equal(16.20m, result.Amount);
        Assert.Equal(string.Empty, result.TransactionType);
    }

    // Verifies dynamic bank keyphrase overrides assign the configured transaction type
    [Fact]
    public void TryParseCsvRow_DynamicKeyphrase_AssignsConfiguredTransactionType()
    {
        string csvLine = "07/27/2026,12345,67890,ONLINE TRANSFER RECEIVED,MICHAEL JORDAN,150.00,";
        var keyphrases = new List<BankKeyPhraseDto>
        {
            new() { KeyPhrase = "ONLINE TRANSFER RECEIVED", TransactionTypeID = "eTransfer" }
        };

        var result = PADLineParser.TryParseCsvRow(csvLine, keyphrases);

        Assert.NotNull(result);
        Assert.True(result.IsValid);
        Assert.Equal(150.00m, result.Amount);
        Assert.Equal("eTransfer", result.TransactionType);
    }

    // Verifies CSV table column header rows return null so they are not parsed as donations
    [Theory]
    [InlineData("Account Type,Account Number,Transaction Date,Cheque Number,Description 1,Description 2,CAD$")]
    [InlineData("Date,Description,Amount,Balance")]
    [InlineData("Transaction Date,Cheque Number,Description,Debit,Credit")]
    public void TryParseCsvRow_CsvHeaders_ReturnsNull(string headerLine)
    {
        var result = PADLineParser.TryParseCsvRow(headerLine, null);

        Assert.Null(result);
    }
}
