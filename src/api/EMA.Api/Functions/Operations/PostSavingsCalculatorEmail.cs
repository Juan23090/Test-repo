using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using EMA.Api.Services.Email;
using QuestPDF.Infrastructure;
using QuestPDF.Fluent;

namespace EMA_Api.Functions.Operations
{
    public class PostSavingsCalculatorEmail
    {
        private readonly IEmailService _emailService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<PostSavingsCalculatorEmail> _logger;

        public PostSavingsCalculatorEmail(
            IEmailService emailService,
            IConfiguration configuration,
            ILogger<PostSavingsCalculatorEmail> logger)
        {
            _emailService = emailService;
            _configuration = configuration;
            _logger = logger;
        }

        [Function("PostSavingsCalculatorEmail")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "savings-calculator/email")] HttpRequestData req)
        {


            SavingsCalculatorEmailRequest? payload;
            try
            {
                payload = await JsonSerializer.DeserializeAsync<SavingsCalculatorEmailRequest>(req.Body, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Invalid savings calculator email payload JSON.");
                var badJsonResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badJsonResponse.WriteAsJsonAsync(new { message = "Invalid JSON payload." });
                return badJsonResponse;
            }

            if (payload == null || string.IsNullOrWhiteSpace(payload.RecipientEmail) || string.IsNullOrWhiteSpace(payload.Subject) || string.IsNullOrWhiteSpace(payload.Body))
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteAsJsonAsync(new { message = "RecipientEmail, Subject, and Body are all required." });
                return badRequestResponse;
            }

            var senderAddress = _configuration["ACS_EMAIL_SENDER"];
            if (string.IsNullOrWhiteSpace(senderAddress))
            {
                _logger.LogError("Missing ACS_EMAIL_SENDER setting.");
                var configErrorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await configErrorResponse.WriteAsJsonAsync(new { message = "Email configuration is missing." });
                return configErrorResponse;
            }

            try
            {
                var encodedBody = System.Net.WebUtility.HtmlEncode(payload.Body)
                    .Replace("\r\n", "<br />")
                    .Replace("\n", "<br />");
                var htmlBody = $"<div style=\"font-family: sans-serif;\">{encodedBody}</div>";

                if (payload.CalculatorData != null)
                {
                    QuestPDF.Settings.License = LicenseType.Community;
                    var pdfBytes = new PostSavingsCalculatorPdf.SavingsCalculatorDocument(payload.CalculatorData).GeneratePdf();
                    var attachmentName = payload.CalculatorData.FileName ?? $"savings-calculator-report-{DateTime.Today:yyyyMMdd}.pdf";

                    await _emailService.SendHtmlEmailWithAttachmentAsync(
                        senderAddress: senderAddress,
                        recipientAddress: payload.RecipientEmail,
                        subject: payload.Subject,
                        htmlBody: htmlBody,
                        attachmentName: attachmentName,
                        attachmentBytes: pdfBytes,
                        plainTextBody: payload.Body,
                        cancellationToken: default);
                }
                else
                {
                    await _emailService.SendHtmlEmailAsync(
                        senderAddress: senderAddress,
                        recipientAddress: payload.RecipientEmail,
                        subject: payload.Subject,
                        htmlBody: htmlBody,
                        plainTextBody: payload.Body,
                        cancellationToken: default);
                }

                var okResponse = req.CreateResponse(HttpStatusCode.OK);
                await okResponse.WriteAsJsonAsync(new { message = "Email sent successfully." });
                return okResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending savings calculator email.");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { message = "Could not send email." });
                return errorResponse;
            }
        }
}

    public sealed class SavingsCalculatorEmailRequest
    {
        public string RecipientEmail { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public PostSavingsCalculatorPdf.SavingsCalculatorPdfRequest? CalculatorData { get; set; }
    }
}

