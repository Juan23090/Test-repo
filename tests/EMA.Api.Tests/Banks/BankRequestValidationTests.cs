using System.Text;
using System.Text.Json;
using EMA.Api.DTOs;
using EMA.Api.Functions.Banks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EMA.Api.Tests.Banks;

public sealed class BankRequestValidationTests
{
    // make sure posting a bank with an invalid or too long bank id gets rejected
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1234567")]
    public async Task PostBankRejectsInvalidBankId(string bankId)
    {
        var function = new SaveBank(NullLogger<SaveBank>.Instance);
        var request = HttpRequest(new CreateBankRequest
        {
            BankID = bankId,
            Name = "Valid Bank Name",
            IsActive = true
        });

        var result = await function.Post(request);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Bank ID is required and must be 6 characters or fewer", badRequest.Value?.ToString());
    }

    // make sure posting a bank with an empty or too long name gets rejected
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PostBankRejectsEmptyName(string name)
    {
        var function = new SaveBank(NullLogger<SaveBank>.Instance);
        var request = HttpRequest(new CreateBankRequest
        {
            BankID = "TD",
            Name = name,
            IsActive = true
        });

        var result = await function.Post(request);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Bank Name is required and must be 50 characters or fewer", badRequest.Value?.ToString());
    }

    // check that posting a bank with a name over 50 characters gets rejected
    [Fact]
    public async Task PostBankRejectsNameOverFiftyCharacters()
    {
        var function = new SaveBank(NullLogger<SaveBank>.Instance);
        var request = HttpRequest(new CreateBankRequest
        {
            BankID = "TD",
            Name = new string('A', 51),
            IsActive = true
        });

        var result = await function.Post(request);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Bank Name is required and must be 50 characters or fewer", badRequest.Value?.ToString());
    }

    // check that putting a bank with an invalid bank id gets rejected
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1234567")]
    public async Task PutBankRejectsInvalidBankId(string bankId)
    {
        var function = new SaveBank(NullLogger<SaveBank>.Instance);
        var request = HttpRequest(new UpdateBankRequest
        {
            Name = "Valid Name",
            IsActive = true
        });

        var result = await function.Put(request, bankId);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Bank ID is invalid", badRequest.Value?.ToString());
    }

    // check that putting a bank with an empty or too long name gets rejected
    [Fact]
    public async Task PutBankRejectsInvalidName()
    {
        var function = new SaveBank(NullLogger<SaveBank>.Instance);
        var request = HttpRequest(new UpdateBankRequest
        {
            Name = new string('X', 51),
            IsActive = true
        });

        var result = await function.Put(request, "TD");

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Bank Name is required and must be 50 characters or fewer", badRequest.Value?.ToString());
    }

    // make sure posting a transaction type requires both bank id and transaction type id
    [Theory]
    [InlineData("", "eTransfer")]
    [InlineData("TD", "")]
    public async Task PostBankTransactionTypeRequiresRequiredFields(string bankId, string typeId)
    {
        var function = new SaveBankTransactionType(NullLogger<SaveBankTransactionType>.Instance);
        var request = HttpRequest(new CreateBankTransactionTypeRequest
        {
            BankId = bankId,
            TransactionTypeID = typeId,
            IsActive = true
        });

        var result = await function.Post(request);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    // make sure posting a transaction type rejects instructions url over 256 characters
    [Fact]
    public async Task PostBankTransactionTypeRejectsOverlongUrl()
    {
        var function = new SaveBankTransactionType(NullLogger<SaveBankTransactionType>.Instance);
        var request = HttpRequest(new CreateBankTransactionTypeRequest
        {
            BankId = "TD",
            TransactionTypeID = "eTransfer",
            InstructionsURL = "https://example.com/" + new string('a', 250),
            IsActive = true
        });

        var result = await function.Post(request);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Instructions URL must be a valid HTTPS URL", badRequest.Value?.ToString());
    }

    // reject unsafe schemes like javascript, data, file, or http when posting a transaction type
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("javascript:void(0)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("file:///etc/passwd")]
    [InlineData("http://example.com")]
    [InlineData("ftp://files.example.com")]
    [InlineData("not-a-valid-url")]
    public async Task PostBankTransactionTypeRejectsUnsafeInstructionsUrlSchemes(string unsafeUrl)
    {
        var function = new SaveBankTransactionType(NullLogger<SaveBankTransactionType>.Instance);
        var request = HttpRequest(new CreateBankTransactionTypeRequest
        {
            BankId = "TD",
            TransactionTypeID = "PAD",
            InstructionsURL = unsafeUrl,
            IsActive = true
        });

        var result = await function.Post(request);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Instructions URL must be a valid HTTPS URL", badRequest.Value?.ToString());
    }

    // verify valid https urls pass validation
    [Theory]
    [InlineData("https://example.com")]
    [InlineData("https://www.rbcroyalbank.com/ways-to-bank/tutorials/statements.html")]
    [InlineData("https://atb.com/support?topic=bank&view=pdf#download")]
    public async Task PostBankTransactionTypeAcceptsValidHttpsUrls(string validUrl)
    {
        var function = new SaveBankTransactionType(NullLogger<SaveBankTransactionType>.Instance);
        var request = HttpRequest(new CreateBankTransactionTypeRequest
        {
            BankId = "TD",
            TransactionTypeID = "PAD",
            InstructionsURL = validUrl,
            IsActive = true
        });

        var result = await function.Post(request);

        Assert.IsNotType<BadRequestObjectResult>(result);
    }

    // make sure updating a transaction type requires a valid positive id
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task PutBankTransactionTypeRequiresPositiveId(int id)
    {
        var function = new SaveBankTransactionType(NullLogger<SaveBankTransactionType>.Instance);
        var request = HttpRequest(new UpdateBankTransactionTypeRequest
        {
            InstructionsURL = "https://example.com",
            IsActive = true
        });

        var result = await function.Put(request, id);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Valid Bank Transaction Type ID is required", badRequest.Value?.ToString());
    }

    // check that updating a transaction type rejects instructions url over 256 characters
    [Fact]
    public async Task PutBankTransactionTypeRejectsOverlongUrl()
    {
        var function = new SaveBankTransactionType(NullLogger<SaveBankTransactionType>.Instance);
        var request = HttpRequest(new UpdateBankTransactionTypeRequest
        {
            InstructionsURL = "https://example.com/" + new string('b', 250),
            IsActive = true
        });

        var result = await function.Put(request, 10);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Instructions URL must be a valid HTTPS URL", badRequest.Value?.ToString());
    }

    // reject unsafe schemes like javascript, data, file, or http when updating a transaction type
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("javascript:void(0)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("file:///etc/passwd")]
    [InlineData("http://example.com")]
    [InlineData("ftp://files.example.com")]
    [InlineData("not-a-valid-url")]
    public async Task PutBankTransactionTypeRejectsUnsafeInstructionsUrlSchemes(string unsafeUrl)
    {
        var function = new SaveBankTransactionType(NullLogger<SaveBankTransactionType>.Instance);
        var request = HttpRequest(new UpdateBankTransactionTypeRequest
        {
            InstructionsURL = unsafeUrl,
            IsActive = true
        });

        var result = await function.Put(request, 10);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Instructions URL must be a valid HTTPS URL", badRequest.Value?.ToString());
    }

    // make sure posting a key phrase requires a valid positive transaction type id
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task PostBankKeyPhraseRequiresPositiveTransactionTypeId(int typeId)
    {
        var function = new SaveBankKeyPhrase(NullLogger<SaveBankKeyPhrase>.Instance);
        var request = HttpRequest(new CreateBankKeyPhraseRequest
        {
            BankTransactionTypeID = typeId,
            KeyPhrase = "AUTODEPOSIT",
            IsActive = true
        });

        var result = await function.Post(request);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Valid Bank Transaction Type ID is required", badRequest.Value?.ToString());
    }

    // make sure posting a key phrase rejects empty or over 100 characters
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PostBankKeyPhraseRejectsEmptyKeyPhrase(string phrase)
    {
        var function = new SaveBankKeyPhrase(NullLogger<SaveBankKeyPhrase>.Instance);
        var request = HttpRequest(new CreateBankKeyPhraseRequest
        {
            BankTransactionTypeID = 1,
            KeyPhrase = phrase,
            IsActive = true
        });

        var result = await function.Post(request);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Key Phrase is required and must be 100 characters or fewer", badRequest.Value?.ToString());
    }

    // check that posting a key phrase over 100 characters is rejected
    [Fact]
    public async Task PostBankKeyPhraseRejectsPhraseOverHundredChars()
    {
        var function = new SaveBankKeyPhrase(NullLogger<SaveBankKeyPhrase>.Instance);
        var request = HttpRequest(new CreateBankKeyPhraseRequest
        {
            BankTransactionTypeID = 1,
            KeyPhrase = new string('K', 101),
            IsActive = true
        });

        var result = await function.Post(request);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Key Phrase is required and must be 100 characters or fewer", badRequest.Value?.ToString());
    }

    // make sure updating a key phrase requires a valid positive id
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task PutBankKeyPhraseRequiresPositiveId(int id)
    {
        var function = new SaveBankKeyPhrase(NullLogger<SaveBankKeyPhrase>.Instance);
        var request = HttpRequest(new UpdateBankKeyPhraseRequest
        {
            KeyPhrase = "AUTODEPOSIT",
            IsActive = true
        });

        var result = await function.Put(request, id);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Valid Bank Key Phrase ID is required", badRequest.Value?.ToString());
    }

    // check that updating a key phrase rejects empty or over 100 characters
    [Fact]
    public async Task PutBankKeyPhraseRejectsInvalidPhrase()
    {
        var function = new SaveBankKeyPhrase(NullLogger<SaveBankKeyPhrase>.Instance);
        var request = HttpRequest(new UpdateBankKeyPhraseRequest
        {
            KeyPhrase = new string('Z', 101),
            IsActive = true
        });

        var result = await function.Put(request, 5);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Key Phrase is required and must be 100 characters or fewer", badRequest.Value?.ToString());
    }

    private static HttpRequest HttpRequest<T>(T payload)
    {
        var json = JsonSerializer.Serialize(payload);
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        context.Request.ContentType = "application/json";
        return context.Request;
    }
}
