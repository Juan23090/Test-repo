using EMA.App.Models;

namespace EMA.App.Services;

public interface IBanksService
{
    Task<BankLookupsModel> GetBankLookupsAsync(CancellationToken cancellationToken = default);

    Task<ServiceResult<BankModel>> CreateBankAsync(
        CreateBankModel request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<BankModel>> UpdateBankAsync(
        string bankId,
        UpdateBankModel request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<BankTransactionTypeModel>> CreateBankTransactionTypeAsync(
        CreateBankTransactionTypeModel request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<bool>> UpdateBankTransactionTypeAsync(
        int id,
        UpdateBankTransactionTypeModel request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<BankKeyPhraseModel>> CreateBankKeyPhraseAsync(
        CreateBankKeyPhraseModel request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<bool>> UpdateBankKeyPhraseAsync(
        int id,
        UpdateBankKeyPhraseModel request,
        CancellationToken cancellationToken = default);
}
