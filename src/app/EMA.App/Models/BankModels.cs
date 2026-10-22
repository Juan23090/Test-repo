namespace EMA.App.Models;

public sealed class BankModel
{
    public string BankID { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public sealed class CreateBankModel
{
    public string BankID { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public sealed class UpdateBankModel
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public sealed class TransactionTypeModel
{
    public string TransactionTypeID { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class BankTransactionTypeModel
{
    public int BankTransactionTypeID { get; set; }
    public string BankId { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string TransactionTypeID { get; set; } = string.Empty;
    public string TransactionTypeDescription { get; set; } = string.Empty;
    public string? InstructionsURL { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class CreateBankTransactionTypeModel
{
    public string BankId { get; set; } = string.Empty;
    public string TransactionTypeID { get; set; } = string.Empty;
    public string? InstructionsURL { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class UpdateBankTransactionTypeModel
{
    public string? InstructionsURL { get; set; }
    public bool IsActive { get; set; }
}

public sealed class BankKeyPhraseModel
{
    // The database column in dbo.BankTransactionTypeKeyPhrase is named BankTransactionTypeKeyPhrase without ID
    public int BankTransactionTypeKeyPhraseID { get; set; }
    public int BankTransactionTypeID { get; set; }
    public string BankId { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string TransactionTypeID { get; set; } = string.Empty;
    public string KeyPhrase { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public sealed class CreateBankKeyPhraseModel
{
    public int BankTransactionTypeID { get; set; }
    public string KeyPhrase { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public sealed class UpdateBankKeyPhraseModel
{
    public string KeyPhrase { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public sealed class BankLookupsModel
{
    public List<BankModel> Banks { get; set; } = new();
    public List<TransactionTypeModel> TransactionTypes { get; set; } = new();
    public List<BankTransactionTypeModel> BankTransactionTypes { get; set; } = new();
    public List<BankKeyPhraseModel> KeyPhrases { get; set; } = new();
}

public sealed class ServiceResult<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? ErrorMessage { get; set; }

    public static ServiceResult<T> Ok(T data) => new() { Success = true, Data = data };
    public static ServiceResult<T> Fail(string error) => new() { Success = false, ErrorMessage = error };
}
