using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EMA.App.Models;

namespace EMA.App.Services;

public interface ISavingsCalculatorService
{
    Task<SavingsCalculatorData?> GetCalculatorDataAsync(int orgId, CancellationToken cancellationToken = default);
    Task<List<ChMSOption>?> GetChMSListAsync(CancellationToken cancellationToken = default);
    Task<bool> SaveSettingsAsync(int orgId, SavingsCalculatorData settings, CancellationToken cancellationToken = default);
    Task<bool> SaveDetailsAsync(int orgId, List<SavingsCalculatorDetailRow> details, CancellationToken cancellationToken = default);
    Task<byte[]?> GeneratePdfAsync(SavingsCalculatorPdfRequest request, CancellationToken cancellationToken = default);
    Task<bool> SendEmailAsync(SavingsCalculatorEmailRequest request, CancellationToken cancellationToken = default);
}

