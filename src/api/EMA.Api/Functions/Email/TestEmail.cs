using System.Net;
using System.Web;
using EMA.Api.Services.Email;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;

namespace EMA.Api.Functions.Email;

public class TestEmail
{
    private readonly IEmailService _emailService;
    private readonly IEmailTemplateService _emailTemplateService;
    private readonly IConfiguration _configuration;

    public TestEmail(
        IEmailService emailService,
        IEmailTemplateService emailTemplateService,
        IConfiguration configuration)
    {
        _emailService = emailService;
        _emailTemplateService = emailTemplateService;
        _configuration = configuration;
    }

    [Function("TestEmail")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "test-email")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var query = HttpUtility.ParseQueryString(req.Url.Query);

        var to = query["to"];
        var donorName = query["name"] ?? "Gabe";

        if (string.IsNullOrWhiteSpace(to))
        {
            var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
            await badResponse.WriteStringAsync("Missing required query parameter: ?to=email@example.com");
            return badResponse;
        }

        var senderAddress = _configuration["ACS_EMAIL_SENDER"];

        if (string.IsNullOrWhiteSpace(senderAddress))
        {
            var configError = req.CreateResponse(HttpStatusCode.InternalServerError);
            await configError.WriteStringAsync("Missing ACS_EMAIL_SENDER setting.");
            return configError;
        }

        var templateValues = new Dictionary<string, string?>
        {
            ["OrganizationName"] = "North Pointe Community Church",

            ["OfficialName"] = "Gabriel Pearse",
            ["ETransferName"] = "GABE PEARSE",
            ["Address"] = "10922 134Ave",
            ["CityTown"] = "Edmonton",
            ["Province"] = "AB",
            ["PostalCode"] = "T5E 1K2",
            ["DonorName"] = "Gabriel Pearse",
            ["DonorEmail"] = "gabe@pearse.name",
            ["DonorPhone"] = "5875684341",

            ["DonationReference"] = "AUTO-DEP-20260703-001",
            ["DonationComments"] = "I would like to donate $12 to General and 13 to Building fund",
            ["DonationCommentsNote"] = "If anything looks wrong, click Make Changes Online before the deadline of",

            ["DonationDeadlineToMakeFundChanges"] = "July 10, 2026",

            ["CampusName"] = "Main Campus",
            ["FundName"] = "General Fund",
            ["DonationAmount"] = "$25.00",
            ["DonationTotal"] = "$25.00",

            ["DefaultCheckmark"] = "✓",

            ["MakeChangesUrl"] = "https://emailmoney.app/donor-login",
            ["GivingHistoryUrl"] = "https://emailmoney.app/donor-login",

            ["FooterText"] = "This email was sent by Email Money App on behalf of North Pointe Community Church."
        };

        var htmlBody = await _emailTemplateService.RenderTemplateAsync(
            "DonationAcknowledgement.html",
            templateValues,
            cancellationToken);

        var subject = _emailTemplateService.RenderString(
            "ACS Dynamic HTML Test for {{DonorName}}",
            templateValues);

        var operationId = await _emailService.SendHtmlEmailAsync(
            senderAddress: senderAddress,
            recipientAddress: to,
            subject: subject,
            htmlBody: htmlBody,
            plainTextBody: "This is the fallback plain-text version of the dynamic ACS email test.",
            cancellationToken: cancellationToken);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteStringAsync($"Email queued successfully. ACS Operation ID: {operationId}");
        return response;
    }
}
