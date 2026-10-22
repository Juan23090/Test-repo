using EMA.App.Models;
using System.Net.Http.Json;
using System.Text.Json;

namespace EMA.App.Services;

public class DonorService : IDonorService
{
    private readonly HttpClient _http;
    private readonly IUserSessionStateService _sessionState;
    private const string ApiBase = "/api";

    public DonorService(
        HttpClient http,
        IUserSessionStateService sessionState)
    {
        _http = http;
        _sessionState = sessionState;
    }

    public async Task<List<DonorItem>> GetAllAsync(int orgID)
    {
        var result = await _http.GetFromJsonAsync<List<DonorItem>>(
            $"{ApiBase}/donors/{orgID}");

        return result ?? new List<DonorItem>();
    }

    public async Task<DonorItem?> GetByIDAsync(int donorID)
    {
        return await _http.GetFromJsonAsync<DonorItem>(
            $"{ApiBase}/donors/single/{donorID}");
    }

    public async Task<DonorItem?> GetOlderAsync(int orgID, int currentID)
    {
        var ordered = (await GetAllAsync(orgID))
            .OrderBy(d => d.EMADonorID)
            .ToList();

        var idx = ordered.FindIndex(d => d.EMADonorID == currentID);

        if (idx <= 0)
            return null;

        return ordered[idx - 1];
    }

    public async Task<DonorItem?> GetNewerAsync(int orgID, int currentID)
    {
        var ordered =
            (await GetAllAsync(orgID))
                .OrderBy(d => d.EMADonorID)
                .ToList();
        var index = ordered.FindIndex(d => d.EMADonorID == currentID);
        if (index < 0 || index >= ordered.Count - 1)
        {
            return null;
        }
        return ordered[index + 1];
    }

    public async Task<int?> CreateAsync(DonorItem donor)
    {
        try
        {
            var payload = new
            {
                EMAOrganizationID = donor.EMAOrganizationID,
                emailAddress = donor.EmailAddress,
                name = donor.OfficialDonorName,
                interacDonorName = donor.InteracDonorName,
                address = donor.Address,
                city = donor.City,
                provinceStateCode = donor.ProvinceStateCode,
                postalCode = donor.PostalCode,
                country = donor.Country,
                phoneNumber = donor.PhoneNumber,
                emaCampusID = donor.EMADefaultCampusID,
                donorStatusID = donor.DonorStatusID,
                isDonorPilotingEtransfers = donor.IsDonorPilotingEtransfers,
                acknowledgmentForManualTransactions = donor.AcknowledgmentForManualTransactions,
                acknowledgementForAutomatedTransactions = donor.AcknowledgementForAutomatedTransactions
            };

            using var request =
                CreateAuthenticatedRequest(
                    HttpMethod.Post,
                    $"{ApiBase}/donors",
                    payload);
            using var response = await _http.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                // The API returns EMADonorID. "donorID" is still accepted so
                // this keeps working against either response shape.
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if ((string.Equals(prop.Name, "EMADonorID", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(prop.Name, "donorID", StringComparison.OrdinalIgnoreCase)) &&
                        prop.Value.TryGetInt32(out var id) &&
                        id > 0)
                    {
                        return id;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Log or handle the exception if needed; silently fail for now.
            System.Diagnostics.Debug.WriteLine($"Error creating donor: {ex.Message}");
        }

        return null;
    }

    public async Task UpdateAsync(DonorItem donor)
    {
        var payload = new
        {
            emailAddress = donor.EmailAddress,
            name = donor.OfficialDonorName,
            interacDonorName = donor.InteracDonorName,
            address = donor.Address,
            city = donor.City,
            provinceStateCode = donor.ProvinceStateCode,
            postalCode = donor.PostalCode,
            country = donor.Country,
            phoneNumber = donor.PhoneNumber,
            emaCampusID = donor.EMADefaultCampusID,
            donorStatusID = donor.DonorStatusID,
            isDonorPilotingEtransfers = donor.IsDonorPilotingEtransfers,
            acknowledgmentForManualTransactions = donor.AcknowledgmentForManualTransactions,
            acknowledgementForAutomatedTransactions = donor.AcknowledgementForAutomatedTransactions
        };

        using var request =
            CreateAuthenticatedRequest(
                HttpMethod.Put,
                $"{ApiBase}/donors/{donor.EMADonorID}",
                payload);
        using var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<DonorItem>> SearchAsync(
        int orgID,
        string query,
        string? field = null,
        bool exact = false,
        CancellationToken cancellationToken = default)
    {
        var url =
            $"{ApiBase}/donors/search/{orgID}?q={Uri.EscapeDataString(query)}";

        if (!string.IsNullOrWhiteSpace(
                field))
        {
            url +=
                $"&field={Uri.EscapeDataString(field)}";
        }

        if (exact)
        {
            url +=
                "&exact=true";
        }

        try
        {
            var result =
                await _http.GetFromJsonAsync<List<DonorItem>>(
                    url,
                    cancellationToken);

            return result ?? [];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return [];
        }
    }

    public async Task<List<JsonElement>> GetDonorAuditAsync(
        int orgID,
        int donorID,
        CancellationToken cancellationToken = default)
    {
        var result =
            await _http.GetFromJsonAsync<List<JsonElement>>(
                $"{ApiBase}/donors/{orgID}/{donorID}/audit",
                cancellationToken);

        return result ?? [];
    }

    public async Task<bool> UpdateJourneyDetailsAsync(int orgID, DonorItem donor)
    {
        try
        {
            var userID = GetCurrentUserID();
            var payload = new
            {
                emailAddress = donor.EmailAddress,
                officialDonorName = donor.OfficialDonorName,
                interacDonorName = donor.InteracDonorName,
                address = donor.Address,
                city = donor.City,
                provinceStateCode = donor.ProvinceStateCode,
                postalCode = donor.PostalCode,
                country = donor.Country,
                phoneNumber = donor.PhoneNumber,
                donorStatusID = donor.DonorStatusID,
                updatedbyUserID = userID
            };
            using var request =
                CreateAuthenticatedRequest(
                    HttpMethod.Put,
                    $"{ApiBase}/donors/journey-update/{orgID}/{donor.EMADonorID}",
                    payload);
            using var response = await _http.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch 
        {
            return false;
        }
    }

    public async Task<List<DonorJourneyActivityType>> GetJourneyActivityTypesAsync(int orgID)
    {
        try
        {
            var result =
                await _http.GetFromJsonAsync<List<DonorJourneyActivityType>>(
                    $"{ApiBase}/donors/activity-types/{orgID}");
            return result ?? new List<DonorJourneyActivityType>();
        }
        catch
        {
            return new List<DonorJourneyActivityType>();
        }
    }

    public async Task<List<UserItem>> GetOrganizationUsersAsync(int orgID)
    {
        try
        {
            var result =
                await _http.GetFromJsonAsync<List<UserItem>>(
                    $"{ApiBase}/donors/users/{orgID}");
            return result ?? new List<UserItem>();
        }
        catch
        {
            return new List<UserItem>();
        }
    }

    public async Task<List<DonorJourneyEntry>> GetJourneyHistoryAsync(int orgID, int donorID)
    {
        try
        {
            var result =
                await _http.GetFromJsonAsync<List<DonorJourneyEntry>>(
                    $"{ApiBase}/donors/journey-history/{orgID}/{donorID}");
            return result ?? new List<DonorJourneyEntry>();
        }
        catch
        {
            return new List<DonorJourneyEntry>();
        }
    }

    public async Task<string?> CreateJourneyEntryAsync(int orgID, int donorID, DonorJourneyEntry entry)
    {
        try
        {
            using var request =
                CreateAuthenticatedRequest(
                    HttpMethod.Post,
                    $"{ApiBase}/donors/journey-history/{orgID}/{donorID}",
                    entry);
            using var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var res = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                if (res.TryGetProperty("donorInteractionID", out var prop))
                {
                    return prop.GetString();
                }
            }
            var result =
                await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            if (result.TryGetProperty("donorInteractionID", out var property))
            {
                return property.GetString();
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private HttpRequestMessage CreateAuthenticatedRequest(
        HttpMethod method,
        string requestUri,
        object payload)
    {
        var userID = GetCurrentUserID();
        var request =
            new HttpRequestMessage(method, requestUri)
            {
                Content = JsonContent.Create(payload)
            };
        request.Headers.TryAddWithoutValidation("X-User-ID", userID.ToString());
        return request;
    }

    private int GetCurrentUserID()
    {
        var userID = _sessionState.CurrentSession?.UserID ?? 0;
        if (userID <= 0)
        {
            throw new InvalidOperationException(
                "A signed-in EMA user is required to modify donor records.");
        }
        return userID;
    }
}
