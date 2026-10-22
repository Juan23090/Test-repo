using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Text.Json;
using EMA.Api.Services.Email;
using EMA.Api.Services.Security;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EMA_Api.Functions.Reports;

/// <summary>
/// POST /api/reports/tributes/{orgID}/donors/email — emails the Tribute Donors report as a PDF.
///
/// Unlike the Savings Calculator email, the caller sends only the filter: the PDF is rebuilt here
/// from the organization's own data, so the attachment can't be forged from the browser.
/// </summary>
public class PostTributeDonorsReportEmail
{
    private static readonly JsonSerializerOptions RequestOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IEmailService _emailService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PostTributeDonorsReportEmail> _logger;
    private readonly string _connectionString;
    private readonly IMenuItemAccessService _menuItemAccess;

    public PostTributeDonorsReportEmail(
        IEmailService emailService,
        IConfiguration configuration,
        ILogger<PostTributeDonorsReportEmail> logger,
        IMenuItemAccessService menuItemAccess)
    {
        _emailService = emailService;
        _configuration = configuration;
        _logger = logger;
        _menuItemAccess = menuItemAccess;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("PostTributeDonorsReportEmail")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "reports/tributes/{orgID:int}/donors/email")] HttpRequestData req,
        int orgID)
    {
        _logger.LogInformation("PostTributeDonorsReportEmail triggered for org {OrgID}.", orgID);

        // The menu permission is a UI gate only until it is enforced here: this endpoint
        // returns donor PII, so require the role that grants the Tribute Report screen.
        if (!await _menuItemAccess.CanAccessMenuItemUrlAsync(
                req.GetRequiredApiIdentity(),
                TributeDonorsReport.MenuItemUrl))
        {
            return await CreateMessage(req, HttpStatusCode.Forbidden, "You do not have access to the Tribute Report.");
        }

        EmailRequest? payload;
        try
        {
            payload = await JsonSerializer.DeserializeAsync<EmailRequest>(req.Body, RequestOptions);
        }
        catch (JsonException)
        {
            return await CreateMessage(req, HttpStatusCode.BadRequest, "Invalid JSON payload.");
        }

        if (payload is null)
        {
            return await CreateMessage(req, HttpStatusCode.BadRequest, "A request body is required.");
        }

        var recipient = payload.RecipientEmail?.Trim() ?? string.Empty;
        if (!MailAddress.TryCreate(recipient, out _))
        {
            return await CreateMessage(req, HttpStatusCode.BadRequest, "A valid recipient email address is required.");
        }

        // Collapse line breaks so the subject can't carry extra header lines.
        var subject = (payload.Subject ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        if (subject.Length == 0)
        {
            return await CreateMessage(req, HttpStatusCode.BadRequest, "A subject is required.");
        }

        if (!TryParseDate(payload.StartDate, out var startDate) || !TryParseDate(payload.EndDate, out var endDate))
        {
            return await CreateMessage(req, HttpStatusCode.BadRequest, "Missing or invalid startDate/endDate. Use yyyy-MM-dd.");
        }

        var candidate = new TributeDonorsReport.Filter(
            payload.TributeTypeID,
            payload.HasName,
            payload.Name,
            startDate,
            endDate);

        if (!TributeDonorsReport.TryValidate(candidate, out var filter, out var error))
        {
            return await CreateMessage(req, HttpStatusCode.BadRequest, error);
        }

        var senderAddress = _configuration["ACS_EMAIL_SENDER"];
        if (string.IsNullOrWhiteSpace(senderAddress))
        {
            _logger.LogError("Missing ACS_EMAIL_SENDER setting.");
            return await CreateMessage(req, HttpStatusCode.InternalServerError, "Email configuration is missing.");
        }

        try
        {
            // The route orgID is already checked against the caller by ApiSecurityMiddleware; read the
            // organization from the identity anyway so the data can never follow a spoofed route.
            var organizationID = req.GetRequiredApiIdentity().EMAOrganizationID;

            var report = await TributeDonorsReport.LoadAsync(_connectionString, organizationID, filter!);
            if (report is null)
            {
                return await CreateMessage(req, HttpStatusCode.NotFound, "Tribute type not found.");
            }

            var pdfBytes = TributeDonorsReport.GeneratePdf(report);

            var message = string.IsNullOrWhiteSpace(payload.Message)
                ? $"Attached is the {report.Title} for {report.DateRange}."
                : payload.Message;

            var htmlBody =
                "<div style=\"font-family: sans-serif;\">" +
                WebUtility.HtmlEncode(message).Replace("\r\n", "<br />").Replace("\n", "<br />") +
                "</div>";

            await _emailService.SendHtmlEmailWithAttachmentAsync(
                senderAddress: senderAddress,
                recipientAddress: recipient,
                subject: subject,
                htmlBody: htmlBody,
                attachmentName: report.FileName,
                attachmentBytes: pdfBytes,
                plainTextBody: message,
                cancellationToken: default);

            // The recipient and report contents are donor PII, so they stay out of the logs.
            _logger.LogInformation("Tribute donors report emailed for org {OrgID}.", organizationID);

            return await CreateMessage(req, HttpStatusCode.OK, "Email sent.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error emailing tribute donors report for org {OrgID}.", orgID);
            return await CreateMessage(req, HttpStatusCode.InternalServerError, "Could not send email.");
        }
    }

    private static bool TryParseDate(string? raw, out DateOnly value)
        => DateOnly.TryParseExact(
            raw,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out value);

    private static async Task<HttpResponseData> CreateMessage(HttpRequestData req, HttpStatusCode status, string message)
    {
        var response = req.CreateResponse(status);
        await response.WriteAsJsonAsync(new { message });
        return response;
    }

    private sealed class EmailRequest
    {
        public string? RecipientEmail { get; set; }
        public string? Subject { get; set; }
        public string? Message { get; set; }
        public int TributeTypeID { get; set; }
        public bool HasName { get; set; }
        public string? Name { get; set; }
        public string? StartDate { get; set; }
        public string? EndDate { get; set; }
    }
}
