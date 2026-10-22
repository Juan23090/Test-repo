using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace EMA.App.Services;

public class DepositReportService : IDepositReportService
{
    private const string ApiBase = "/api";
    private readonly HttpClient _http;

    public DepositReportService(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<ChequeReportRow>> GetChequesAsync(int orgID, DateTime inputDate, CancellationToken cancellationToken = default)
    {
        var route = $"{ApiBase}/reports/cheques/{orgID}/{inputDate:yyyy-MM-dd}";
        var payload = await _http.GetFromJsonAsync<List<Dictionary<string, JsonElement>>>(route, cancellationToken);

        if (payload is null || payload.Count == 0)
            return Array.Empty<ChequeReportRow>();

        var rows = new List<ChequeReportRow>(payload.Count);
        foreach (var item in payload)
        {
            var name = ReadString(item, "Name");
            var referenceNumber = ReadString(item, "#");
            var amount = ReadDecimal(item, "Amount");
            rows.Add(new ChequeReportRow(name, referenceNumber, amount));
        }

        return rows;
    }

    private static string ReadString(Dictionary<string, JsonElement> source, string key)
    {
        if (!source.TryGetValue(key, out var value) || value.ValueKind == JsonValueKind.Null)
            return string.Empty;

        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : value.ToString();
    }

    private static decimal ReadDecimal(Dictionary<string, JsonElement> source, string key)
    {
        if (!source.TryGetValue(key, out var value) || value.ValueKind == JsonValueKind.Null)
            return 0m;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var numeric))
            return numeric;

        return decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0m;
    }
}