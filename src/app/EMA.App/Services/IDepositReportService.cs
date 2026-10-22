namespace EMA.App.Services;

public interface IDepositReportService
{
    Task<IReadOnlyList<ChequeReportRow>> GetChequesAsync(int orgID, DateTime inputDate, CancellationToken cancellationToken = default);
}

public sealed record ChequeReportRow(string Name, string ReferenceNumber, decimal Amount);