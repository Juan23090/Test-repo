namespace EMA.Api.DTOs;

public sealed class BankDto
{
    public string BankID { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public sealed class CreateBankRequest
{
    public string BankID { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public sealed class UpdateBankRequest
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public sealed class TransactionTypeDto
{
    public string TransactionTypeID { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class BankTransactionTypeDto
{
    public int BankTransactionTypeID { get; set; }
    public string BankId { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string TransactionTypeID { get; set; } = string.Empty;
    public string TransactionTypeDescription { get; set; } = string.Empty;
    public string? InstructionsURL { get; set; }
    public bool IsActive { get; set; }
}

public sealed class CreateBankTransactionTypeRequest
{
    public string BankId { get; set; } = string.Empty;
    public string TransactionTypeID { get; set; } = string.Empty;
    public string? InstructionsURL { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class UpdateBankTransactionTypeRequest
{
    public string? InstructionsURL { get; set; }
    public bool IsActive { get; set; }
}

public sealed class BankKeyPhraseDto
{
    // The database column in dbo.BankTransactionTypeKeyPhrase is named BankTransactionTypeKeyPhrase without ID
    public int BankTransactionTypeKeyPhraseID { get; set; }
    public int BankTransactionTypeID { get; set; }
    public string BankId { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string TransactionTypeID { get; set; } = string.Empty;
    public string KeyPhrase { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public sealed class CreateBankKeyPhraseRequest
{
    public int BankTransactionTypeID { get; set; }
    public string KeyPhrase { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public sealed class UpdateBankKeyPhraseRequest
{
    public string KeyPhrase { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public sealed class BankLookupsResponseDto
{
    public List<BankDto> Banks { get; set; } = new();
    public List<TransactionTypeDto> TransactionTypes { get; set; } = new();
    public List<BankTransactionTypeDto> BankTransactionTypes { get; set; } = new();
    public List<BankKeyPhraseDto> KeyPhrases { get; set; } = new();
}

public static class BankMappingHelper
{
    public static bool ToBool(string? value)
    {
        return string.Equals(value?.Trim(), "Y", StringComparison.OrdinalIgnoreCase);
    }

    public static string ToActiveChar(bool isActive)
    {
        return isActive ? "Y" : "N";
    }

    public static string TrimOrEmpty(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    public static bool IsValidInstructionsUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > 256)
        {
            return false;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttps;
    }
}
