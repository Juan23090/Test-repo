namespace EMA.Api.Services.Authentication;

public static class ProvinceStateCodeNormalizer
{
    private static readonly IReadOnlyDictionary<string, string> NameToCode =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Alberta"] = "AB",
            ["British Columbia"] = "BC",
            ["Manitoba"] = "MB",
            ["New Brunswick"] = "NB",
            ["Newfoundland and Labrador"] = "NL",
            ["Newfoundland & Labrador"] = "NL",
            ["Nova Scotia"] = "NS",
            ["Northwest Territories"] = "NT",
            ["Nunavut"] = "NU",
            ["Ontario"] = "ON",
            ["Prince Edward Island"] = "PE",
            ["Quebec"] = "QC",
            ["Québec"] = "QC",
            ["Saskatchewan"] = "SK",
            ["Yukon"] = "YT",
            ["Alabama"] = "AL",
            ["Alaska"] = "AK",
            ["Arizona"] = "AZ",
            ["Arkansas"] = "AR",
            ["California"] = "CA",
            ["Colorado"] = "CO",
            ["Connecticut"] = "CT",
            ["Delaware"] = "DE",
            ["District of Columbia"] = "DC",
            ["Florida"] = "FL",
            ["Georgia"] = "GA",
            ["Hawaii"] = "HI",
            ["Idaho"] = "ID",
            ["Illinois"] = "IL",
            ["Indiana"] = "IN",
            ["Iowa"] = "IA",
            ["Kansas"] = "KS",
            ["Kentucky"] = "KY",
            ["Louisiana"] = "LA",
            ["Maine"] = "ME",
            ["Maryland"] = "MD",
            ["Massachusetts"] = "MA",
            ["Michigan"] = "MI",
            ["Minnesota"] = "MN",
            ["Mississippi"] = "MS",
            ["Missouri"] = "MO",
            ["Montana"] = "MT",
            ["Nebraska"] = "NE",
            ["Nevada"] = "NV",
            ["New Hampshire"] = "NH",
            ["New Jersey"] = "NJ",
            ["New Mexico"] = "NM",
            ["New York"] = "NY",
            ["North Carolina"] = "NC",
            ["North Dakota"] = "ND",
            ["Ohio"] = "OH",
            ["Oklahoma"] = "OK",
            ["Oregon"] = "OR",
            ["Pennsylvania"] = "PA",
            ["Rhode Island"] = "RI",
            ["South Carolina"] = "SC",
            ["South Dakota"] = "SD",
            ["Tennessee"] = "TN",
            ["Texas"] = "TX",
            ["Utah"] = "UT",
            ["Vermont"] = "VT",
            ["Virginia"] = "VA",
            ["Washington"] = "WA",
            ["West Virginia"] = "WV",
            ["Wisconsin"] = "WI",
            ["Wyoming"] = "WY"
        };

    private static readonly HashSet<string> ValidCodes =
        NameToCode.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed =
            value.Trim();

        if (NameToCode.TryGetValue(
                trimmed,
                out var mappedCode))
        {
            return mappedCode;
        }

        var upperCode =
            trimmed.ToUpperInvariant();

        return ValidCodes.Contains(upperCode)
            ? upperCode
            : null;
    }
}
