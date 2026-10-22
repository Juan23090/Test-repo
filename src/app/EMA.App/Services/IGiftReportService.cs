using System.Threading;
using System.Threading.Tasks;
using EMA.App.Models;

namespace EMA.App.Services;

public interface IGiftReportService
{
    Task<GiftReportResponse> GetGiftReportAsync(
        int orgID,
        DateOnly startDate,
        DateOnly endDate,
        string timePeriod,
        string classification,
        string metric,
        CancellationToken cancellationToken = default);
}

/// <summary>Wrapper carrying success/error around the GiftReportResult payload (mirrors TributeReportResult).</summary>
public sealed class GiftReportResponse
{
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
    public GiftReportResult? Data { get; init; }
}
