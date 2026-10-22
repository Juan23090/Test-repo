using System.Threading;
using System.Threading.Tasks;
using EMA.App.Models;

namespace EMA.App.Services;

public interface IEmaStatisticsService
{
    Task<EmaStatisticsResponse> GetStatisticsAsync(
        DateOnly startDate,
        DateOnly endDate,
        string timePeriod,
        string classification,
        string scope,
        string measurement,
        CancellationToken cancellationToken = default);
}

/// <summary>Wrapper carrying success/error around the EmaStatisticsResult payload (mirrors GiftReportResponse).</summary>
public sealed class EmaStatisticsResponse
{
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
    public EmaStatisticsResult? Data { get; init; }
}
