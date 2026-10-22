using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Primitives;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace EMA_Api.Functions.Reports;

/// <summary>
/// Shared by the Tribute Donors JSON, PDF and email endpoints so all three validate the same
/// filter, run the same query and render the same document.
/// </summary>
internal static class TributeDonorsReport
{
    // Matches GetTributeReport, whose rows link here, so every drill-down it produces is valid.
    // The screen these endpoints back. Callers must hold a role granting it; resolved by URL
    // rather than MenuItemID because menu item IDs have been renumbered before.
    internal const string MenuItemUrl = "/tribute-report";

    private const int MaxRangeDays = 366;

    private const string UnnamedLabel = "(no name given)";

    internal sealed record Filter(
        int TributeTypeID,
        bool HasName,
        string? Name,
        DateOnly StartDate,
        DateOnly EndDate);

    internal sealed class Donor
    {
        public string Name { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string Phone { get; init; } = string.Empty;
        public string Address { get; init; } = string.Empty;
        public string City { get; init; } = string.Empty;
        public string Province { get; init; } = string.Empty;
        public string PostalCode { get; init; } = string.Empty;
    }

    internal sealed class Report
    {
        public string OrganizationName { get; init; } = "Organization";
        public string TypeDescription { get; init; } = string.Empty;
        public string? DateFormat { get; init; }
        public Filter Filter { get; init; } = null!;
        public List<Donor> Donors { get; init; } = new();

        public string Title =>
            $"Tribute Donors Report - {TypeDescription} {(Filter.HasName ? Filter.Name : UnnamedLabel)}";

        public string DateRange =>
            $"{FormatDate(Filter.StartDate, DateFormat)} - {FormatDate(Filter.EndDate, DateFormat)}";

        public string FileName =>
            $"tribute-donors-{Filter.StartDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}-{Filter.EndDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.pdf";
    }

    internal static bool TryReadFilter(
        IReadOnlyDictionary<string, StringValues> query,
        out Filter? filter,
        out string error)
    {
        filter = null;

        if (!query.TryGetValue("tributeTypeID", out var rawType) ||
            !int.TryParse(rawType.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var typeID))
        {
            error = "Missing or invalid tributeTypeID.";
            return false;
        }

        if (!query.TryGetValue("hasName", out var rawHasName) ||
            !bool.TryParse(rawHasName.ToString(), out var hasName))
        {
            error = "Missing or invalid hasName. Use true or false.";
            return false;
        }

        if (!TryReadDate(query, "startDate", out var startDate))
        {
            error = "Missing or invalid startDate. Use yyyy-MM-dd.";
            return false;
        }

        if (!TryReadDate(query, "endDate", out var endDate))
        {
            error = "Missing or invalid endDate. Use yyyy-MM-dd.";
            return false;
        }

        var name = query.TryGetValue("name", out var rawName) ? rawName.ToString() : null;

        return TryValidate(new Filter(typeID, hasName, name, startDate, endDate), out filter, out error);
    }

    internal static bool TryValidate(Filter candidate, out Filter? filter, out string error)
    {
        filter = null;

        if (candidate.TributeTypeID <= 0)
        {
            error = "Missing or invalid tributeTypeID.";
            return false;
        }

        if (candidate.HasName && string.IsNullOrEmpty(candidate.Name))
        {
            error = "A name is required when hasName is true.";
            return false;
        }

        if (candidate.HasName && candidate.Name!.Length > 255)
        {
            error = "The tribute name is too long.";
            return false;
        }

        if (candidate.EndDate < candidate.StartDate)
        {
            error = "endDate must be on or after startDate.";
            return false;
        }

        if (candidate.EndDate.DayNumber - candidate.StartDate.DayNumber > MaxRangeDays)
        {
            error = "The report date range cannot exceed 366 days.";
            return false;
        }

        // An unnamed filter never carries a name, so a stray value can't change what it matches.
        filter = candidate with { Name = candidate.HasName ? candidate.Name : null };
        error = string.Empty;
        return true;
    }

    /// <returns><c>null</c> when the tribute type doesn't belong to the organization.</returns>
    internal static async Task<Report?> LoadAsync(string connectionString, int orgID, Filter filter)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();

        string organizationName;
        string? dateFormat;
        string typeDescription;

        // Resolving the type inside the organization doubles as a tenant check on tributeTypeID.
        await using (var header = new SqlCommand(
            @"SELECT o.OrganizationName, o.DateFormatID, ott.Description
                FROM dbo.Organization o
                INNER JOIN dbo.OrganizationTributeType ott
                    ON ott.EMAOrganizationID = o.EMAOrganizationID
                   AND ott.EMATributeTypeID = @TributeTypeID
               WHERE o.EMAOrganizationID = @OrgID;",
            conn))
        {
            header.Parameters.AddWithValue("@OrgID", orgID);
            header.Parameters.AddWithValue("@TributeTypeID", filter.TributeTypeID);

            await using var reader = await header.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return null;
            }

            organizationName = reader.IsDBNull(0) ? "Organization" : reader.GetString(0);
            dateFormat = reader.IsDBNull(1) ? null : reader.GetString(1).Trim();
            typeDescription = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
        }

        var donors = new List<Donor>();

        await using (var cmd = new SqlCommand("dbo.sp_GetTributeDonorsReport", conn)
        {
            CommandType = CommandType.StoredProcedure
        })
        {
            cmd.Parameters.AddWithValue("@OrganizationID", orgID);
            cmd.Parameters.AddWithValue("@TributeTypeID", filter.TributeTypeID);
            cmd.Parameters.Add("@HasName", SqlDbType.Bit).Value = filter.HasName;
            cmd.Parameters.Add("@TributeText", SqlDbType.VarChar, 255).Value = (object?)filter.Name ?? DBNull.Value;
            cmd.Parameters.Add("@StartDate", SqlDbType.Date).Value = filter.StartDate.ToDateTime(TimeOnly.MinValue);
            cmd.Parameters.Add("@EndDate", SqlDbType.Date).Value = filter.EndDate.ToDateTime(TimeOnly.MinValue);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                donors.Add(new Donor
                {
                    Name = ReadText(reader, "OfficialDonorName"),
                    Email = ReadText(reader, "EmailAddress"),
                    Phone = ReadText(reader, "PhoneNumber"),
                    Address = ReadText(reader, "Address"),
                    City = ReadText(reader, "City"),
                    Province = ReadText(reader, "ProvinceStateCode"),
                    PostalCode = ReadText(reader, "PostalCode")
                });
            }
        }

        return new Report
        {
            OrganizationName = organizationName,
            TypeDescription = typeDescription,
            DateFormat = dateFormat,
            Filter = filter,
            Donors = donors
        };
    }

    internal static byte[] GeneratePdf(Report report)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        return new Document(report).GeneratePdf();
    }

    private static string ReadText(SqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal)
            ? string.Empty
            : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
    }

    private static bool TryReadDate(IReadOnlyDictionary<string, StringValues> query, string key, out DateOnly value)
    {
        value = default;

        return query.TryGetValue(key, out var raw) &&
               !string.IsNullOrWhiteSpace(raw) &&
               DateOnly.TryParseExact(
                   raw.ToString(),
                   "yyyy-MM-dd",
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.None,
                   out value);
    }

    private static string FormatDate(DateOnly date, string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        try
        {
            return date.ToString(pattern, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
    }

    private sealed class Document : IDocument
    {
        private static readonly string[] ColumnTitles =
            { "Donor", "Email", "Phone", "Address", "City/Town", "PS", "Postal" };

        private readonly Report _report;

        public Document(Report report)
        {
            _report = report;
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                // Seven columns of contact details need the width of a landscape page.
                page.Size(PageSizes.Letter.Landscape());
                page.Margin(16);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor("#202124"));

                page.Content().Column(col =>
                {
                    col.Spacing(10);

                    col.Item().AlignCenter().Text(_report.OrganizationName).Bold().FontSize(20);
                    col.Item().AlignCenter().Text(_report.Title).SemiBold().FontSize(13);
                    col.Item().AlignCenter().Text(_report.DateRange).FontSize(11);

                    if (_report.Donors.Count == 0)
                    {
                        col.Item().PaddingTop(8).AlignCenter()
                            .Text("No donors found for this tribute in the selected range.").SemiBold();
                        return;
                    }

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(2.2f); // Donor
                            columns.RelativeColumn(2.6f); // Email
                            columns.RelativeColumn(1.4f); // Phone
                            columns.RelativeColumn(2.6f); // Address
                            columns.RelativeColumn(1.5f); // City/Town
                            columns.ConstantColumn(34);   // PS
                            columns.RelativeColumn(1f);   // Postal
                        });

                        // Repeats on every page, so long donor lists stay readable.
                        table.Header(header =>
                        {
                            foreach (var title in ColumnTitles)
                            {
                                header.Cell().Element(HeaderCell).Text(title).SemiBold();
                            }
                        });

                        foreach (var donor in _report.Donors)
                        {
                            table.Cell().Element(DataCell).Text(donor.Name);
                            table.Cell().Element(DataCell).Text(donor.Email);
                            table.Cell().Element(DataCell).Text(donor.Phone);
                            table.Cell().Element(DataCell).Text(donor.Address);
                            table.Cell().Element(DataCell).Text(donor.City);
                            table.Cell().Element(DataCell).Text(donor.Province);
                            table.Cell().Element(DataCell).Text(donor.PostalCode);
                        }
                    });

                    col.Item().AlignRight().Text(
                        _report.Donors.Count == 1 ? "1 donor" : $"{_report.Donors.Count} donors").FontSize(9);
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.CurrentPageNumber();
                    text.Span(" / ");
                    text.TotalPages();
                });
            });
        }

        private static IContainer HeaderCell(IContainer container)
            => container
                .Border(1)
                .BorderColor("#8a8a8a")
                .Background("#dcdcdc")
                .PaddingVertical(6)
                .PaddingHorizontal(6);

        private static IContainer DataCell(IContainer container)
            => container
                .Border(1)
                .BorderColor("#8a8a8a")
                .PaddingVertical(5)
                .PaddingHorizontal(6);
    }
}
