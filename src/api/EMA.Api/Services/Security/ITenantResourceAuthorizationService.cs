using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace EMA.Api.Services.Security;

public interface
    ITenantResourceAuthorizationService
{
    Task<bool> AreResourcesOwnedByTenantAsync(
        string functionName,
        IReadOnlyDictionary<string, object?>
            bindingData,
        IQueryCollection query,
        JsonElement? requestBody,
        ApiRequestIdentity identity,
        CancellationToken cancellationToken = default);
}
