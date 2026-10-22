using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace EMA_Api.Functions.Reports;

public class PostYearEndReceiptPdf
{
    private readonly ILogger<PostYearEndReceiptPdf> _logger;

    public PostYearEndReceiptPdf(ILogger<PostYearEndReceiptPdf> logger)
    {
        _logger = logger;
    }

    [Function("PostYearEndReceiptPdf")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "reports/year-end-receipt/pdf")] HttpRequestData req)
    {


        YearEndReceiptPdfRequest? payload;
        try
        {
            payload = await JsonSerializer.DeserializeAsync<YearEndReceiptPdfRequest>(req.Body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid year-end receipt payload JSON.");
            var badJsonResponse = req.CreateResponse(HttpStatusCode.BadRequest);
            await badJsonResponse.WriteAsJsonAsync(new { message = "Invalid JSON payload." });
            return badJsonResponse;
        }

        if (!TryValidate(payload, out var validationMessage, out var safePayload))
        {
            var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequestResponse.WriteAsJsonAsync(new { message = validationMessage });
            return badRequestResponse;
        }

        try
        {
            QuestPDF.Settings.License = LicenseType.Community;
            var pdfBytes = new YearEndReceiptDocument(safePayload).GeneratePdf();
            var fileName = BuildFileName(safePayload.FileName, safePayload.RecipientName);

            var okResponse = req.CreateResponse(HttpStatusCode.OK);
            okResponse.Headers.Add("Content-Type", "application/pdf");
            okResponse.Headers.Add("Content-Disposition", $"attachment; filename=\"{fileName}\"");

            await okResponse.Body.WriteAsync(pdfBytes);
            return okResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating year-end receipt PDF.");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { message = "Could not generate PDF." });
            return errorResponse;
        }
    }

    private static bool TryValidate(
        YearEndReceiptPdfRequest? payload,
        out string validationMessage,
        out YearEndReceiptPdfRequest safePayload)
    {
        validationMessage = string.Empty;
        safePayload = payload ?? new YearEndReceiptPdfRequest();

        if (payload is null)
        {
            validationMessage = "Request payload is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(payload.OrganizationName))
        {
            validationMessage = "organizationName is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(payload.RecipientName))
        {
            validationMessage = "recipientName is required.";
            return false;
        }

        // CRA-mandatory fields for official receipts (official + official-draft). This endpoint
        // is the single chokepoint both the org screen and the donor self-serve download funnel
        // through, so validating here can't be bypassed by a direct POST. Unofficial is exempt.
        var isOfficial = string.Equals(payload.Mode, "official", StringComparison.OrdinalIgnoreCase);
        var isOfficialDraft = string.Equals(payload.Mode, "official-draft", StringComparison.OrdinalIgnoreCase);
        if (isOfficial || isOfficialDraft)
        {
            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(payload.ReceiptIssuedIn)) missing.Add("place issued (city)");
            if (string.IsNullOrWhiteSpace(payload.RegistrationNumber)) missing.Add("CRA registration number");
            if (payload.ReceiptSignatureGraphic is null || payload.ReceiptSignatureGraphic.Length == 0) missing.Add("authorized receipt signature");
            // A final official receipt needs its serial number; a draft legitimately shows "Draft".
            if (isOfficial && string.IsNullOrWhiteSpace(payload.ReceiptNumber)) missing.Add("receipt serial number");

            if (missing.Count > 0)
            {
                validationMessage = $"Official receipts require: {string.Join(", ", missing)}. The CRA mandates these on official donation receipts.";
                return false;
            }
        }

        if (payload.OrganizationAddressLine is null ||
            payload.OrganizationContactLine is null ||
            payload.LetterBody is null ||
            payload.OrganizationName.Length > 200 ||
            payload.RecipientName.Length > 200 ||
            payload.OrganizationAddressLine.Length > 500 ||
            payload.OrganizationContactLine.Length > 500 ||
            payload.LetterBody.Length > 20_000 ||
            payload.Gifts?.Count > 500 ||
            payload.Gifts?.Any(
                gift =>
                    gift is null ||
                    gift.Date is null ||
                    gift.Date.Length > 50 ||
                    gift.Fund is null ||
                    gift.Fund.Length > 500) ==
                true ||
            payload.LetterSignatureGraphic?.Length >
                2L * 1024 * 1024 ||
            payload.ReceiptSignatureGraphic?.Length >
                2L * 1024 * 1024)
        {
            validationMessage =
                "The PDF payload exceeds the supported limits.";
            return false;
        }

        safePayload = payload with
        {
            Gifts = payload.Gifts ?? new List<YearEndReceiptGiftRow>()
        };

        return true;
    }

    private static string BuildFileName(string? preferredName, string recipientName)
    {
        var baseName = string.IsNullOrWhiteSpace(preferredName)
            ? $"ye-receipt-{recipientName}"
            : preferredName;

        baseName =
            new string(
                baseName
                    .Take(100)
                    .Select(
                        character =>
                            char.IsLetterOrDigit(
                                character) ||
                            character is
                                '-' or '_' or
                                '.' or ' '
                                ? character
                                : '-')
                    .ToArray())
                .Trim(
                    ' ',
                    '.',
                    '-');
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "ye-receipt";
        }

        return baseName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            ? baseName
            : $"{baseName}.pdf";
    }

    private sealed class YearEndReceiptDocument : IDocument
    {
        // At least 12 rows per column for the classic look, but grow to fit every gift so no
        // receipted donation is dropped from the summary (the two columns hold 2x this).
        private int SummaryRowsPerTable => Math.Max(12, (int)Math.Ceiling(_payload.Gifts.Count / 2.0));
        private readonly YearEndReceiptPdfRequest _payload;

        public YearEndReceiptDocument(YearEndReceiptPdfRequest payload)
        {
            _payload = payload;
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(20);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Content().Column(col =>
                {
                    col.Spacing(10);

                    ComposeLetterSection(col.Item());
                    ComposeSummarySection(col.Item());
                });
            });
        }

        private void ComposeLetterSection(IContainer container)
        {
            container.Border(1).BorderColor(Colors.Grey.Lighten1).Padding(14).Column(col =>
            {
                col.Spacing(6);

                col.Item().AlignCenter().Text(_payload.OrganizationName).Bold().FontSize(24);
                col.Item().AlignCenter().Text(_payload.OrganizationAddressLine);
                col.Item().AlignCenter().Text(_payload.OrganizationContactLine);

                // CRA requires the charity's registration number on official receipts.
                if (!string.IsNullOrWhiteSpace(_payload.RegistrationNumber))
                {
                    col.Item().AlignCenter().Text($"Charity Registration Number: {_payload.RegistrationNumber}");
                }

                col.Item().PaddingTop(8).Text(_payload.DateText);
                col.Item().Text($"Dear {_payload.RecipientName}");

                col.Item().Border(1).BorderColor(Colors.Grey.Lighten1).MinHeight(110).Padding(8).Element(c =>
                {
                    c.Text(text =>
                    {
                        foreach (var line in SplitBodyLines(_payload.LetterBody))
                        {
                            text.Line(line);
                        }
                    });
                });

                col.Item().PaddingTop(8).Text("Sincerely,");

                col.Item().PaddingTop(4).Row(row =>
                {
                    row.RelativeItem().Column(signatureCol =>
                    {
                        signatureCol.Spacing(4);
                        signatureCol.Item().Row(inner =>
                        {
                            inner.ConstantItem(90).Element(LabelCell).Text("Letter Signature").Bold().FontSize(9);
                            inner.RelativeItem().Element(ValueCell).Element(container => RenderSignature(container, _payload.LetterSignatureGraphic, _payload.LetterSignatureTitle));
                        });

                        signatureCol.Item().Row(inner =>
                        {
                            inner.ConstantItem(90).Element(LabelCell).Text("Title").Bold().FontSize(9);
                            inner.RelativeItem().Element(ValueCell).PaddingHorizontal(6).PaddingVertical(4).Text(_payload.LetterSignatureTitle);
                        });
                    });

                    row.RelativeItem().Column(signatureCol =>
                    {
                        signatureCol.Spacing(4);
                        signatureCol.Item().Row(inner =>
                        {
                            inner.ConstantItem(90).Element(LabelCell).Text("Receipt Signature").Bold().FontSize(9);
                            inner.RelativeItem().Element(ValueCell).Element(container => RenderSignature(container, _payload.ReceiptSignatureGraphic, _payload.ReceiptSignatureTitle));
                        });

                        signatureCol.Item().Row(inner =>
                        {
                            inner.ConstantItem(90).Element(LabelCell).Text("Title").Bold().FontSize(9);
                            inner.RelativeItem().Element(ValueCell).PaddingHorizontal(6).PaddingVertical(4).Text(_payload.ReceiptSignatureTitle);
                        });
                    });
                });

                col.Item().PaddingTop(4).Row(row =>
                {
                    row.RelativeItem().Text(_payload.DonorAddress);
                    row.ConstantItem(230).Column(receiptCol =>
                    {
                        receiptCol.Spacing(2);
                        // Draft prints "Draft" where the number goes; unofficial replaces the whole
                        // line with "(Unofficial Receipt)"; official prints the real receipt number.
                        var receiptLine = _payload.Mode switch
                        {
                            "official-draft" => "RECEIPT # Draft",
                            "unofficial" => "(Unofficial Receipt)",
                            _ => $"RECEIPT # {_payload.ReceiptNumber}"
                        };
                        // Soften to match the donor-address column on the left (Roger's request),
                        // and drop the separate Unique identifier line — it is the same value as the
                        // receipt number (CRA serial number), so showing both is redundant.
                        receiptCol.Item().Text(receiptLine);
                        receiptCol.Item().Text($"Year Received: {_payload.YearReceived}");
                        receiptCol.Item().Text($"Date Receipted: {_payload.DateReceipted}");
                        // CRA requires the city/town/municipality where the receipt was issued
                        // (official receipts only).
                        if (!string.Equals(_payload.Mode, "unofficial", StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrWhiteSpace(_payload.ReceiptIssuedIn))
                        {
                            receiptCol.Item().Text($"Receipt issued in: {_payload.ReceiptIssuedIn}");
                        }
                    });
                });

                // Unofficial receipts get the "other charities" note instead of the official CRA line.
                var isUnofficial = string.Equals(_payload.Mode, "unofficial", StringComparison.OrdinalIgnoreCase);
                var footerText = isUnofficial
                    ? "(Unofficial Receipt for Gifts From or Via Other Charities)"
                    : _payload.ReceiptFooterText;
                col.Item().PaddingTop(8).Text(footerText).Bold();

                // CRA requires its name and website on official receipts.
                if (!isUnofficial)
                {
                    col.Item().PaddingTop(2).Text("Canada Revenue Agency: canada.ca/charities-giving").FontSize(9);
                }
            });
        }

        private void ComposeSummarySection(IContainer container)
        {
            container.Border(1).BorderColor(Colors.Grey.Lighten1).Padding(10).Column(col =>
            {
                col.Spacing(8);
                col.Item().Text("SUMMARY OF GIFTS").Bold().FontSize(16);

                var rowsPerTable = SummaryRowsPerTable;
                var leftRows = _payload.Gifts.Take(rowsPerTable).ToList();
                var rightRows = _payload.Gifts.Skip(rowsPerTable).Take(rowsPerTable).ToList();

                col.Item().Row(row =>
                {
                    row.Spacing(8);
                    row.RelativeItem().Element(c => ComposeSummaryTable(c, leftRows, rowsPerTable));
                    row.RelativeItem().Element(c => ComposeSummaryTable(c, rightRows, rowsPerTable));
                });

                // Total of every listed gift — computed from the rows so it always matches the summary.
                var giftTotal = _payload.Gifts.Sum(gift => gift.Amount);
                col.Item().AlignRight().PaddingTop(4)
                    .Text($"Total Eligible Amount: {FormatAmount(giftTotal)}").Bold().FontSize(13);
            });
        }

        private static void ComposeSummaryTable(IContainer container, IReadOnlyList<YearEndReceiptGiftRow> rows, int rowsPerTable)
        {
            container.Border(1).BorderColor(Colors.Grey.Lighten1).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(1);
                    columns.RelativeColumn(1.7f);
                    columns.RelativeColumn(1);
                });

                table.Header(header =>
                {
                    header.Cell().Element(CellStyle).Text("DATE").Bold();
                    header.Cell().Element(CellStyle).Text("FUND").Bold();
                    header.Cell().Element(CellStyle).AlignRight().Text("AMOUNT").Bold();
                });

                for (var i = 0; i < rowsPerTable; i++)
                {
                    var row = i < rows.Count ? rows[i] : null;
                    table.Cell().Element(CellStyle).Text(row?.Date ?? string.Empty);
                    table.Cell().Element(CellStyle).Text(row?.Fund ?? string.Empty);
                    table.Cell().Element(CellStyle).AlignRight().Text(row is null ? string.Empty : FormatAmount(row.Amount));
                }
            });
        }

        private static void RenderSignature(IContainer container, byte[]? signatureGraphic, string signatureTitle)
        {
            container.PaddingHorizontal(6).PaddingVertical(4).Column(col =>
            {
                col.Spacing(2);

                if (signatureGraphic is { Length: > 0 })
                {
                    col.Item().Height(44).Image(signatureGraphic);
                }
            });
        }

        private static IContainer CellStyle(IContainer container)
            => container.BorderBottom(1).BorderColor(Colors.Grey.Lighten1).PaddingHorizontal(4).PaddingVertical(3);

        private static IContainer LabelCell(IContainer container)
            => container.Background("#4f83d1").PaddingHorizontal(6).PaddingVertical(4).DefaultTextStyle(x => x.FontColor(Colors.White));

        private static IContainer ValueCell(IContainer container)
            => container.Border(1).BorderColor(Colors.Grey.Lighten1).PaddingHorizontal(6).PaddingVertical(4);

        private static IEnumerable<string> SplitBodyLines(string? body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                yield return string.Empty;
                yield break;
            }

            var normalized = body.Replace("\r\n", "\n");
            foreach (var line in normalized.Split('\n'))
            {
                yield return line;
            }
        }

        private static string FormatAmount(decimal amount)
            => amount.ToString("C", CultureInfo.GetCultureInfo("en-CA"));
    }

    private sealed record YearEndReceiptPdfRequest
    {
        public string OrganizationName { get; init; } = string.Empty;
        public string OrganizationAddressLine { get; init; } = string.Empty;
        public string OrganizationContactLine { get; init; } = string.Empty;
        public string RegistrationNumber { get; init; } = string.Empty;
        public string DateText { get; init; } = "Date";
        public string RecipientName { get; init; } = string.Empty;
        public string LetterBody { get; init; } = string.Empty;
        public string DonorAddress { get; init; } = string.Empty;
        public string ReceiptNumber { get; init; } = string.Empty;
        public string YearReceived { get; init; } = string.Empty;
        public string DateReceipted { get; init; } = string.Empty;
        public string ReceiptIssuedIn { get; init; } = "Canada";
        public string UniqueIdentifier { get; init; } = string.Empty;
        public decimal DonationAmount { get; init; }
        // "official" | "official-draft" | "unofficial" — controls how the receipt-number line and
        // the bottom-left footer render (see YearEndReceiptDocument).
        public string Mode { get; init; } = "official";
        public string ReceiptFooterText { get; init; } = "OFFICIAL RECEIPT FOR INCOME TAX PURPOSES";
        public byte[]? LetterSignatureGraphic { get; init; }
        public string LetterSignatureTitle { get; init; } = string.Empty;
        public byte[]? ReceiptSignatureGraphic { get; init; }
        public string ReceiptSignatureTitle { get; init; } = string.Empty;
        public List<YearEndReceiptGiftRow> Gifts { get; init; } = new();
        public string? FileName { get; init; }
    }

    private sealed record YearEndReceiptGiftRow
    {
        public string Date { get; init; } = string.Empty;
        public string Fund { get; init; } = string.Empty;
        public decimal Amount { get; init; }
    }
}
