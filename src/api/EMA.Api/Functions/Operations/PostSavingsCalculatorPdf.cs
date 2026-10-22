using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace EMA_Api.Functions.Operations;

public class PostSavingsCalculatorPdf
{
    private readonly ILogger<PostSavingsCalculatorPdf> _logger;

    public PostSavingsCalculatorPdf(ILogger<PostSavingsCalculatorPdf> logger)
    {
        _logger = logger;
    }

    [Function("PostSavingsCalculatorPdf")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "savings-calculator/pdf")] HttpRequestData req)
    {


        SavingsCalculatorPdfRequest? payload;
        try
        {
            payload = await JsonSerializer.DeserializeAsync<SavingsCalculatorPdfRequest>(req.Body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid savings calculator PDF payload JSON.");
            var badJsonResponse = req.CreateResponse(HttpStatusCode.BadRequest);
            await badJsonResponse.WriteAsJsonAsync(new { message = "Invalid JSON payload." });
            return badJsonResponse;
        }

        if (payload == null)
        {
            var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequestResponse.WriteAsJsonAsync(new { message = "Payload is required." });
            return badRequestResponse;
        }

        if (payload.OrganizationName is null ||
            payload.DatePeriodText is null ||
            payload.Wording is null ||
            payload.ChMSName is null ||
            payload.Details is null ||
            payload.Details.Count > 100 ||
            payload.OrganizationName.Length > 200 ||
            payload.DatePeriodText.Length > 200 ||
            payload.Wording.Length > 1000 ||
            payload.ChMSName.Length > 200 ||
            payload.Details.Any(
                row =>
                    row is null ||
                    row.Description is null ||
                    row.Description.Length >
                    500))
        {
            var invalidPayloadResponse =
                req.CreateResponse(
                    HttpStatusCode.BadRequest);
            await invalidPayloadResponse
                .WriteAsJsonAsync(
                    new
                    {
                        message =
                            "The PDF payload exceeds the supported limits."
                    });

            return invalidPayloadResponse;
        }

        try
        {
            QuestPDF.Settings.License = LicenseType.Community;

            var pdfBytes = new SavingsCalculatorDocument(payload).GeneratePdf();
            var fileName =
                BuildFileName(
                    payload.FileName,
                    $"savings-calculator-report-{DateTime.Today:yyyyMMdd}");

            var okResponse = req.CreateResponse(HttpStatusCode.OK);
            okResponse.Headers.Add("Content-Type", "application/pdf");
            okResponse.Headers.Add("Content-Disposition", $"attachment; filename=\"{fileName}\"");

            await okResponse.Body.WriteAsync(pdfBytes);
            return okResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating savings calculator PDF.");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { message = "Could not generate PDF." });
            return errorResponse;
        }
    }

    private static string BuildFileName(
        string? preferredName,
        string fallbackName)
    {
        var rawName =
            string.IsNullOrWhiteSpace(
                preferredName)
                ? fallbackName
                : preferredName.Trim();

        var safeName =
            new string(
                rawName
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

        if (string.IsNullOrWhiteSpace(
                safeName))
        {
            safeName =
                fallbackName;
        }

        return safeName.EndsWith(
            ".pdf",
            StringComparison.OrdinalIgnoreCase)
            ? safeName
            : $"{safeName}.pdf";
    }

    public sealed class SavingsCalculatorDocument : IDocument
    {
        private readonly SavingsCalculatorPdfRequest _payload;

        public SavingsCalculatorDocument(SavingsCalculatorPdfRequest payload)
        {
            _payload = payload;
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter.Landscape());
                page.Margin(18);
                page.DefaultTextStyle(x => x.FontSize(8.5f).FontColor("#202124").FontFamily("Arial"));

                page.Header().Border(1).BorderColor("#ced4da").Background("#ffffff").Padding(10).Column(col =>
                {
                    col.Spacing(2);
                    col.Item().AlignCenter().Text(_payload.OrganizationName).Bold().FontSize(15);
                    col.Item().AlignCenter().Text("Potential Savings with EMA Giving").Bold().FontSize(11).FontColor("#495057");
                    
                    var dates = _payload.DatePeriodText.Split(new[] { " to " }, StringSplitOptions.None);
                    if (dates.Length == 2)
                    {
                        col.Item().AlignCenter().Row(row =>
                        {
                            row.Spacing(4);
                            row.AutoItem().Text("For the Period").FontSize(9).FontColor("#6c757d");
                            
                            row.AutoItem().Border(0.5f).BorderColor("#ced4da").Background("#ffffff").PaddingHorizontal(4).PaddingVertical(1).Row(r =>
                            {
                                r.Spacing(4);
                                r.AutoItem().Text(dates[0]).FontSize(9);
                                r.AutoItem().Element(RenderCalendarIcon);
                            });

                            row.AutoItem().Text("to").FontSize(9).FontColor("#6c757d");

                            row.AutoItem().Border(0.5f).BorderColor("#ced4da").Background("#ffffff").PaddingHorizontal(4).PaddingVertical(1).Row(r =>
                            {
                                r.Spacing(4);
                                r.AutoItem().Text(dates[1]).FontSize(9);
                                r.AutoItem().Element(RenderCalendarIcon);
                            });
                        });
                    }
                    else
                    {
                        col.Item().AlignCenter().Text($"For the Period {_payload.DatePeriodText}").FontSize(9).FontColor("#6c757d");
                    }
                    
                    col.Item().PaddingTop(4).AlignCenter().Text(_payload.Wording).Bold().FontSize(9).FontColor("#0000FF");
                });

                page.Content().PaddingTop(12).Column(col =>
                {
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3.2f);
                            columns.RelativeColumn(1.8f);
                            columns.RelativeColumn(1.1f);
                            columns.RelativeColumn(1.6f);
                            columns.RelativeColumn(1.5f);
                            columns.RelativeColumn(1.3f);
                            columns.RelativeColumn(1.6f);
                            columns.RelativeColumn(1.1f);
                            columns.RelativeColumn(1.4f);
                            columns.RelativeColumn(1.8f);
                            columns.RelativeColumn(1.8f);
                            columns.RelativeColumn(1.6f);
                            columns.RelativeColumn(1.6f);
                            columns.RelativeColumn(2.0f);
                        });

                        table.Header(header =>
                        {
                            header.Cell().ColumnSpan(1).Element(c => HeaderSpanCell(c, "#d9d9d9")).AlignCenter().Text("ChMS").Bold();
                            header.Cell().ColumnSpan(10).Element(c => HeaderSpanCell(c, "#d9e1f2")).AlignCenter().Text("Current").Bold();
                            header.Cell().ColumnSpan(3).Element(c => HeaderSpanCell(c, "#c6e0b4")).AlignCenter().Text("With EMA Giving").Bold();

                            header.Cell().ColumnSpan(1).Element(c => SubHeaderCell(c, "#d9d9d9")).Padding(2).Border(0.5f).BorderColor("#868e96").Background("#ffffff").Padding(4).Row(row =>
                            {
                                row.RelativeItem().Text(_payload.ChMSName).Bold();
                                row.AutoItem().Text("▼").FontSize(6).FontColor("#495057");
                            });
                            header.Cell().ColumnSpan(3).Element(c => SubHeaderCell(c, "#d9e1f2")).AlignCenter().Text("Percentage Based").Bold();
                            header.Cell().ColumnSpan(3).Element(c => SubHeaderCell(c, "#d9e1f2")).AlignCenter().Text("Transaction Based").Bold();
                            header.Cell().ColumnSpan(3).Element(c => SubHeaderCell(c, "#d9e1f2")).AlignCenter().Text("Office").Bold();
                            header.Cell().RowSpan(2).Element(c => ColumnNameCell(c, "#d9e1f2")).AlignCenter().Text("Total").Bold();
                            header.Cell().RowSpan(2).Element(c => ColumnNameCell(c, "#c6e0b4")).AlignCenter().Text("Change To\nEMA\nGiving").Bold();
                            header.Cell().RowSpan(2).Element(c => ColumnNameCell(c, "#c6e0b4")).AlignCenter().Text("EMA\nCharge").Bold();
                            header.Cell().RowSpan(2).Element(c => ColumnNameCell(c, "#c6e0b4")).AlignCenter().Text("Potential\nSavings").Bold();

                            header.Cell().Element(c => ColumnNameCell(c, "#d9e1f2")).Text("Method of Giving").Bold();
                            header.Cell().Element(c => ColumnNameCell(c, "#d9e1f2")).AlignRight().Text("Value").Bold();
                            header.Cell().Element(c => ColumnNameCell(c, "#d9e1f2")).AlignRight().Text("%").Bold();
                            header.Cell().Element(c => ColumnNameCell(c, "#d9e1f2")).AlignRight().Text("Charge").Bold();
                            header.Cell().Element(c => ColumnNameCell(c, "#d9e1f2")).AlignRight().Text("# Txs").Bold();
                            header.Cell().Element(c => ColumnNameCell(c, "#d9e1f2")).AlignRight().Text("Per Tx").Bold();
                            header.Cell().Element(c => ColumnNameCell(c, "#d9e1f2")).AlignRight().Text("Charge").Bold();
                            header.Cell().Element(c => ColumnNameCell(c, "#d9e1f2")).AlignRight().Text("Min").Bold();
                            header.Cell().Element(c => ColumnNameCell(c, "#d9e1f2")).AlignRight().Text("$ Per\nMin").Bold();
                            header.Cell().Element(c => ColumnNameCell(c, "#d9e1f2")).AlignRight().Text("Labor Cost").Bold();
                        });

                        var standardRows = _payload.Details
                            .Where(r => !r.Description.Equals("Software Rental", StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        for (int i = 0; i < standardRows.Count; i++)
                        {
                            var row = standardRows[i];
                            var rowNum = i + 1;
                            var rowBg = (rowNum % 2 == 1) ? "#ffffff" : "#f2f2f2";

                            table.Cell().Element(c => DataCell(c, rowBg)).Text(row.Description).Bold();
                            table.Cell().Element(c => DataCell(c, "#f2f8fc")).AlignRight().Text(FormatMoney(row.Value)).Bold().FontColor("#0000FF");
                            table.Cell().Element(c => DataCell(c, rowBg)).AlignRight().Text((row.PercentBasedFee * 100m).ToString("0.#") + "%");
                            table.Cell().Element(c => DataCell(c, rowBg)).AlignRight().Text(FormatMoney(row.PercentBasedCharge));
                            table.Cell().Element(c => DataCell(c, "#f2f8fc")).AlignRight().Text(row.NumberOfTransactions.ToString("N0")).Bold().FontColor("#0000FF");
                            table.Cell().Element(c => DataCell(c, rowBg)).AlignRight().Text(row.PerTransactionFee.ToString("C2"));
                            table.Cell().Element(c => DataCell(c, rowBg)).AlignRight().Text(FormatOptionalCents(row.TransactionBasedCharge));
                            table.Cell().Element(c => DataCell(c, "#f2f8fc")).AlignRight().Text(row.LaborMinutes.ToString("0.##")).Bold().FontColor("#0000FF");
                            table.Cell().Element(c => DataCell(c, rowBg)).AlignRight().Text((_payload.LaborCostPerHour / 60m).ToString("C2"));
                            table.Cell().Element(c => DataCell(c, rowBg)).AlignRight().Text(FormatMoney(row.LaborCost));
                            table.Cell().Element(c => DataCell(c, rowBg)).AlignRight().Text(FormatMoney(row.TotalCost));
                            
                            table.Cell().Element(c => DataCell(c, rowBg)).AlignCenter().Element(container => RenderToggle(container, row.ChangeToEma));
                            table.Cell().Element(c => DataCell(c, rowBg)).AlignRight().Text(FormatMoney(row.EmaCharge));
                            table.Cell().Element(c => DataCell(c, rowBg)).AlignRight().Text(FormatMoney(row.PotentialSavings));
                        }

                        table.Cell().Element(SubtotalCell).Text("Subtotals").Bold();
                        table.Cell().Element(SubtotalCell).AlignRight().Text(FormatMoney(_payload.SubtotalValue)).Bold();
                        table.Cell().Element(SubtotalCell);
                        table.Cell().Element(SubtotalCell);
                        table.Cell().Element(SubtotalCell).AlignRight().Text(_payload.SubtotalTxs.ToString("N0")).Bold();
                        table.Cell().Element(SubtotalCell);
                        table.Cell().Element(SubtotalCell);
                        table.Cell().Element(SubtotalCell).AlignRight().Text(_payload.SubtotalMinutes.ToString("0.##")).Bold();
                        table.Cell().Element(SubtotalCell);
                        table.Cell().Element(SubtotalCell).AlignRight().Text(FormatMoney(_payload.SubtotalLaborCost)).Bold();
                        table.Cell().Element(SubtotalCell).AlignRight().Text(FormatMoney(_payload.SubtotalTotal)).Bold();
                        table.Cell().Element(SubtotalCell).Text("");
                        table.Cell().Element(SubtotalCell).AlignRight().Text(FormatMoney(_payload.SubtotalEmaCharge)).Bold();
                        table.Cell().Element(SubtotalCell).Text("");

                        var softwareRentalRow = _payload.Details
                            .FirstOrDefault(r => r.Description.Equals("Software Rental", StringComparison.OrdinalIgnoreCase));

                        if (softwareRentalRow != null)
                        {
                            table.Cell().Element(c => RentalCell(c, "#ffffff")).Text(softwareRentalRow.Description).Bold();
                            table.Cell().Element(c => RentalCell(c, "#ffffff"));
                            table.Cell().Element(c => RentalCell(c, "#ffffff"));
                            table.Cell().Element(c => RentalCell(c, "#f2f8fc")).AlignRight().Text(FormatMoney(softwareRentalRow.PercentBasedCharge)).Bold().FontColor("#0000FF");
                            table.Cell().Element(c => RentalCell(c, "#ffffff")).AlignRight().Text("/Mo");
                            table.Cell().Element(c => RentalCell(c, "#ffffff")).AlignRight().Text("x");
                            table.Cell().Element(c => RentalCell(c, "#ffffff")).AlignRight().Text(_payload.NumberOfMonths.ToString());
                            table.Cell().Element(c => RentalCell(c, "#ffffff")).AlignRight().Text("Mo");
                            table.Cell().Element(c => RentalCell(c, "#ffffff")).AlignRight().Text("=");
                            table.Cell().Element(c => RentalCell(c, "#ffffff"));
                            table.Cell().Element(c => RentalCell(c, "#ffffff")).AlignRight().Text(FormatMoney(softwareRentalRow.TotalCost)).Bold();
                            table.Cell().Element(c => RentalCell(c, "#ffffff"));
                            table.Cell().Element(c => RentalCell(c, "#ffffff"));
                            table.Cell().Element(c => RentalCell(c, "#ffffff")).AlignRight().Text(FormatMoney(softwareRentalRow.PotentialSavings)).Bold();
                        }

                        table.Cell().ColumnSpan(10).Element(TotalCell).AlignRight().Text("Totals").Bold();
                        table.Cell().Element(TotalCell).AlignRight().Text(FormatMoney(_payload.GrandTotal)).Bold();
                        table.Cell().Element(TotalCell);
                        table.Cell().Element(TotalCell).AlignRight().Text(FormatMoney(_payload.SubtotalEmaCharge)).Bold();
                        table.Cell().Element(TotalCell).AlignRight().Text(FormatMoney(_payload.GrandSavings)).Bold();
                    });
                });
            });
        }

        private static IContainer HeaderSpanCell(IContainer container, string bgColor)
            => container
                .Border(0.5f)
                .BorderColor("#ced4da")
                .Background(bgColor)
                .PaddingVertical(4)
                .PaddingHorizontal(4);

        private static IContainer SubHeaderCell(IContainer container, string bgColor)
            => container
                .Border(0.5f)
                .BorderColor("#ced4da")
                .Background(bgColor)
                .PaddingVertical(4)
                .PaddingHorizontal(4);

        private static IContainer ColumnNameCell(IContainer container, string bgColor)
            => container
                .BorderBottom(1f)
                .BorderColor("#868e96")
                .Background(bgColor)
                .PaddingVertical(4)
                .PaddingHorizontal(4);

        private static IContainer DataCell(IContainer container, string bgColor)
            => container
                .BorderBottom(0.5f)
                .BorderColor("#dee2e6")
                .Background(bgColor)
                .PaddingVertical(4)
                .PaddingHorizontal(4);

        private static IContainer RentalCell(IContainer container, string bgColor)
            => container
                .BorderBottom(0.5f)
                .BorderColor("#dee2e6")
                .Background(bgColor)
                .PaddingVertical(4)
                .PaddingHorizontal(4);

        private static IContainer SubtotalCell(IContainer container)
            => container
                .BorderBottom(1)
                .BorderColor("#868e96")
                .Background("#f2f2f2")
                .PaddingVertical(5)
                .PaddingHorizontal(4);

        private static IContainer TotalCell(IContainer container)
            => container
                .BorderTop(1.5f)
                .BorderBottom(1.5f)
                .BorderColor("#495057")
                .Background("#f2f2f2")
                .PaddingVertical(5)
                .PaddingHorizontal(4);

        private static void RenderToggle(IContainer container, string changeToEma)
        {
            var isYes = changeToEma.Equals("Y", StringComparison.OrdinalIgnoreCase);
            container.AlignCenter().Row(row =>
            {
                row.Spacing(4);
                row.AutoItem().Width(24).Height(12).CornerRadius(6).Background(isYes ? "#4caf50" : "#dcdcdc").Padding(1).Row(inner =>
                {
                    if (isYes)
                    {
                        inner.RelativeItem();
                        inner.ConstantItem(10).Height(10).CornerRadius(5).Background("#ffffff");
                    }
                    else
                    {
                        inner.ConstantItem(10).Height(10).CornerRadius(5).Background("#ffffff");
                        inner.RelativeItem();
                    }
                });
                row.AutoItem().Text(isYes ? "Yes" : "No");
            });
        }

        private static void RenderCalendarIcon(IContainer container)
        {
            container.Width(10).Height(10).Border(0.8f).BorderColor("#495057").Column(col =>
            {
                col.Item().Height(3).Background("#495057");
            });
        }

        private static string FormatMoney(decimal amount)
            => amount.ToString("C0", CultureInfo.GetCultureInfo("en-CA"));

        private static string FormatOptionalCents(decimal val)
        {
            return val % 1 == 0 ? val.ToString("C0", CultureInfo.GetCultureInfo("en-CA")) : val.ToString("C2", CultureInfo.GetCultureInfo("en-CA"));
        }
    }

    public sealed class SavingsCalculatorPdfRequest
    {
        public string OrganizationName { get; set; } = string.Empty;
        public string DatePeriodText { get; set; } = string.Empty;
        public string ChMSName { get; set; } = string.Empty;
        public decimal LaborCostPerHour { get; set; }
        public decimal MonthlySoftwareRental { get; set; }
        public int NumberOfMonths { get; set; }
        public string Wording { get; set; } = string.Empty;
        public List<SavingsCalculatorPdfRow> Details { get; set; } = new();
        public decimal SubtotalValue { get; set; }
        public int SubtotalTxs { get; set; }
        public double SubtotalMinutes { get; set; }
        public decimal SubtotalLaborCost { get; set; }
        public decimal SubtotalTotal { get; set; }
        public decimal SubtotalEmaCharge { get; set; }
        public decimal GrandTotal { get; set; }
        public decimal GrandSavings { get; set; }
        public string? FileName { get; set; }
    }

    public sealed class SavingsCalculatorPdfRow
    {
        public string Description { get; set; } = string.Empty;
        public decimal Value { get; set; }
        public decimal PercentBasedFee { get; set; }
        public decimal PercentBasedCharge { get; set; }
        public int NumberOfTransactions { get; set; }
        public decimal PerTransactionFee { get; set; }
        public decimal TransactionBasedCharge { get; set; }
        public decimal LaborMinutes { get; set; }
        public decimal LaborCost { get; set; }
        public decimal TotalCost { get; set; }
        public string ChangeToEma { get; set; } = "N";
        public decimal EmaCharge { get; set; }
        public decimal PotentialSavings { get; set; }
    }
}
