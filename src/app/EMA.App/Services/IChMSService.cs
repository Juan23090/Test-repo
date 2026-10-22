using EMA.App.Models;

namespace EMA.App.Services;

public interface IChMSService
{
    Task<List<ChMSSummary>> GetChMSAsync(CancellationToken cancellationToken = default);

    // Dropdown option lists for Candidate For / Candidate Rating / API Rating.
    Task<ChMSLookups> GetLookupsAsync(CancellationToken cancellationToken = default);

    // Returns the new ChMSID, or null on failure.
    Task<int?> InsertChMSAsync(ChMSSummary chms, CancellationToken cancellationToken = default);

    // Returns true on success.
    Task<bool> UpdateChMSAsync(ChMSSummary chms, CancellationToken cancellationToken = default);
}
