using System.Net;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using EMA.Api.Services.Security;

namespace EMA_Api.Functions.Reports;

/// <summary>
/// GET /api/reports/tributes/{orgID}/donors — the distinct donors behind one Tribute Report row:
/// a tribute type plus a name, or that type's unnamed tributes when hasName=false.
/// Query: tributeTypeID, hasName, name, startDate, endDate (yyyy-MM-dd).
/// </summary>
public class GetTributeDonorsReport
{
    private readonly ILogger<GetTributeDonorsReport> _logger;
    private readonly string _connectionString;
    private readonly IMenuItemAccessService _menuItemAccess;

    public GetTributeDonorsReport(
        ILogger<GetTributeDonorsReport> logger,
        IMenuItemAccessService menuItemAccess)
    {
        _logger = logger;
        _menuItemAccess = menuItemAccess;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("GetTributeDonorsReport")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "reports/tributes/{orgID:int}/donors")] HttpRequestData req,
        int orgID)
    {
        _logger.LogInformation("GetTributeDonorsReport triggered for org {OrgID}.", orgID);

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

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                report.OrganizationName,
                report.TypeDescription,
                filter!.Name,
                filter.HasName,
                report.Title,
                report.Donors
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading tribute donors for org {OrgID}.", orgID);
            return await CreateMessage(req, HttpStatusCode.InternalServerError, "Unable to load tribute donors.");
        }
    }

    private static async Task<HttpResponseData> CreateMessage(HttpRequestData req, HttpStatusCode status, string message)
    {
        var response = req.CreateResponse(status);
        await response.WriteAsJsonAsync(new { message });
        return response;
    }
}
