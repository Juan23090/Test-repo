using EMA.App.Models;

namespace EMA.App.Services;

public interface IAccountingSoftwareService
{
    Task<List<AccountingSoftwareSummary>> GetAccountingSoftwareAsync(CancellationToken cancellationToken = default);
}