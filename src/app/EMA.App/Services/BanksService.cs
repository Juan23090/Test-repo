using System.Net.Http.Json;
using System.Text.Json;
using EMA.App.Models;

namespace EMA.App.Services;

public class BanksService : IBanksService
{
    private const string ApiBase = "/api";
    private readonly HttpClient _http;

    public BanksService(HttpClient http)
    {
        _http = http;
    }

    public async Task<BankLookupsModel> GetBankLookupsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<BankLookupsModel>(
                $"{ApiBase}/banks/lookups",
                cancellationToken) ?? new BankLookupsModel();
        }
        catch
        {
            return new BankLookupsModel();
        }
    }

    public async Task<ServiceResult<BankModel>> CreateBankAsync(
        CreateBankModel request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync(
                $"{ApiBase}/banks",
                request,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<BankModel>(cancellationToken: cancellationToken);
                return result is not null
                    ? ServiceResult<BankModel>.Ok(result)
                    : ServiceResult<BankModel>.Fail("Failed to read server response.");
            }

            var error = await ExtractErrorMessageAsync(response, "Failed to create bank.", cancellationToken);
            return ServiceResult<BankModel>.Fail(error);
        }
        catch (Exception ex)
        {
            return ServiceResult<BankModel>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<BankModel>> UpdateBankAsync(
        string bankId,
        UpdateBankModel request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.PutAsJsonAsync(
                $"{ApiBase}/banks/{Uri.EscapeDataString(bankId)}",
                request,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<BankModel>(cancellationToken: cancellationToken);
                return result is not null
                    ? ServiceResult<BankModel>.Ok(result)
                    : ServiceResult<BankModel>.Fail("Failed to read server response.");
            }

            var error = await ExtractErrorMessageAsync(response, "Failed to update bank.", cancellationToken);
            return ServiceResult<BankModel>.Fail(error);
        }
        catch (Exception ex)
        {
            return ServiceResult<BankModel>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<BankTransactionTypeModel>> CreateBankTransactionTypeAsync(
        CreateBankTransactionTypeModel request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync(
                $"{ApiBase}/banks/transaction-types",
                request,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<BankTransactionTypeModel>(cancellationToken: cancellationToken);
                return result is not null
                    ? ServiceResult<BankTransactionTypeModel>.Ok(result)
                    : ServiceResult<BankTransactionTypeModel>.Fail("Failed to read server response.");
            }

            var error = await ExtractErrorMessageAsync(response, "Failed to create bank transaction type.", cancellationToken);
            return ServiceResult<BankTransactionTypeModel>.Fail(error);
        }
        catch (Exception ex)
        {
            return ServiceResult<BankTransactionTypeModel>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<bool>> UpdateBankTransactionTypeAsync(
        int id,
        UpdateBankTransactionTypeModel request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.PutAsJsonAsync(
                $"{ApiBase}/banks/transaction-types/{id}",
                request,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return ServiceResult<bool>.Ok(true);
            }

            var error = await ExtractErrorMessageAsync(response, "Failed to update bank transaction type.", cancellationToken);
            return ServiceResult<bool>.Fail(error);
        }
        catch (Exception ex)
        {
            return ServiceResult<bool>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<BankKeyPhraseModel>> CreateBankKeyPhraseAsync(
        CreateBankKeyPhraseModel request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync(
                $"{ApiBase}/banks/key-phrases",
                request,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<BankKeyPhraseModel>(cancellationToken: cancellationToken);
                return result is not null
                    ? ServiceResult<BankKeyPhraseModel>.Ok(result)
                    : ServiceResult<BankKeyPhraseModel>.Fail("Failed to read server response.");
            }

            var error = await ExtractErrorMessageAsync(response, "Failed to create bank key phrase.", cancellationToken);
            return ServiceResult<BankKeyPhraseModel>.Fail(error);
        }
        catch (Exception ex)
        {
            return ServiceResult<BankKeyPhraseModel>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<bool>> UpdateBankKeyPhraseAsync(
        int id,
        UpdateBankKeyPhraseModel request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.PutAsJsonAsync(
                $"{ApiBase}/banks/key-phrases/{id}",
                request,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return ServiceResult<bool>.Ok(true);
            }

            var error = await ExtractErrorMessageAsync(response, "Failed to update bank key phrase.", cancellationToken);
            return ServiceResult<bool>.Fail(error);
        }
        catch (Exception ex)
        {
            return ServiceResult<bool>.Fail(ex.Message);
        }
    }

    private static async Task<string> ExtractErrorMessageAsync(
        HttpResponseMessage response,
        string fallback,
        CancellationToken cancellationToken)
    {
        try
        {
            var doc = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            if (doc.TryGetProperty("message", out var msg) && !string.IsNullOrWhiteSpace(msg.GetString()))
            {
                return msg.GetString()!;
            }
        }
        catch
        {
        }

        return fallback;
    }
}
