using System.Net;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using EMA.Api.Services.Security;

namespace EMA_Api.Functions.Reports;

/// <summary>
/// GET /api/reports/tributes/{orgID}/donors/pdf — the Tribute Donors report as a PDF download.
/// Takes the same query as <see cref="GetTributeDonorsReport"/>.
/// </summary>
public class GetTributeDonorsReportPdf
{
    private readonly ILogger<GetTributeDonorsReportPdf> _logger;
    private readonly string _connectionString;
    private readonly IMenuItemAccessService _menuItemAccess;

    public GetTributeDonorsReportPdf(
        ILogger<GetTributeDonorsReportPdf> logger,
        IMenuItemAccessService menuItemAccess)
    {
        _logger = logger;
        _menuItemAccess = menuItemAccess;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("GetTributeDonorsReportPdf")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "reports/tributes/{orgID:int}/donors/pdf")] HttpRequestData req,
        int orgID)
    {
        _logger.LogInformation("GetTributeDonorsReportPdf triggered for org {OrgID}.", orgID);

        // The menu permission is a UI gate only until it is enforced here: this endpoint
        // returns donor PII, so require the role that grants the Tribute Report screen.
        if (!await _menuItemAccess.CanAccessMenuItemUrlAsync(
                req.GetRequiredApiIdentity(),
                TributeDonorsReport.MenuItemUrl))
        {
            return await CreateMessage(req, HttpStatusCode.Forbidden, "You do not have access to the Tribute Report.");
        }

        var query = QueryHelpers.ParseQuery(req.Url.Query);

        if (!TributeDonorsReport.TryReadFilter(query, out var filter, out var error))
        {
            return await CreateMessage(req, HttpStatusCode.BadRequest, error);
        }

        try
        {
            var report = await TributeDonorsReport.LoadAsync(_connectionString, orgID, filter!);
            if (report is null)
            {
                return await CreateMessage(req, HttpStatusCode.NotFound, "Tribute type not found.");
            }

            var pdfBytes = TributeDonorsReport.GeneratePdf(report);

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/pdf");
            response.Headers.Add("Content-Disposition", $"attachment; filename=\"{report.FileName}\"");
            await response.Body.WriteAsync(pdfBytes);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating tribute donors PDF for org {OrgID}.", orgID);
            return await CreateMessage(req, HttpStatusCode.InternalServerError, "Could not generate PDF.");
        }
    }

    private static async Task<HttpResponseData> CreateMessage(HttpRequestData req, HttpStatusCode status, string message)
    {
        var response = req.CreateResponse(status);
        await response.WriteAsJsonAsync(new { message });
        return response;
    }
}
