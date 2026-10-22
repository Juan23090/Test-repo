using EMA.App.Models;

namespace EMA.App.Services;

public interface IDateFormatService
{
    Task<List<DateFormatSummary>> GetDateFormatsAsync(
        CancellationToken cancellationToken = default);
}