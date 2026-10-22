using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using EMA.App.Models;

namespace EMA.App.Services;

public class SavingsCalculatorService : ISavingsCalculatorService
{
    private const string ApiBase = "/api/savings-calculator";
    private readonly HttpClient _http;

    public SavingsCalculatorService(HttpClient http)
    {
        _http = http;
    }

    public async Task<SavingsCalculatorData?> GetCalculatorDataAsync(int orgId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<SavingsCalculatorData>($"{ApiBase}/{orgId}", cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<ChMSOption>?> GetChMSListAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<ChMSOption>>($"{ApiBase}/chms-options", cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> SaveSettingsAsync(int orgId, SavingsCalculatorData settings, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new
            {
                StartDate = settings.StartDate,
                EndDate = settings.EndDate,
                LaborCostPerHour = settings.LaborCostPerHour,
                MonthlySoftwareRental = settings.MonthlySoftwareRental,
                NumberOfMonths = settings.NumberOfMonths,
                ChMSID = settings.ChMSID
            };
            var response = await _http.PostAsJsonAsync($"{ApiBase}/settings/{orgId}", payload, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> SaveDetailsAsync(int orgId, List<SavingsCalculatorDetailRow> details, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync($"{ApiBase}/details/{orgId}", details, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<byte[]?> GeneratePdfAsync(SavingsCalculatorPdfRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync($"{ApiBase}/pdf", request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsByteArrayAsync(cancellationToken);
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> SendEmailAsync(SavingsCalculatorEmailRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync($"{ApiBase}/email", request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}

