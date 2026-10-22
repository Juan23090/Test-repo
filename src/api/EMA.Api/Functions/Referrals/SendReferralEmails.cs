using System.Net;
using System.Text.Json;
using EMA.Api.Models;
using EMA.Api.Services.Referrals;
using EMA.Api.Services.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Referrals;

public sealed class SendReferralEmails
{
    private readonly IReferralService _referralService;
    private readonly ILogger<SendReferralEmails> _logger;

    public SendReferralEmails(
        IReferralService referralService,
        ILogger<SendReferralEmails> logger)
    {
        _referralService = referralService;
        _logger = logger;
    }

    [Function("SendReferralEmails")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "organization/{orgID:int}/referrals/send")]
        HttpRequest req,
        int orgID,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Sending batch referral emails for organization {OrgID}", orgID);

        // ApiSecurityMiddleware has already authenticated the caller and
        // rejected an orgID route value belonging to another tenant, so the
        // identity is authoritative here.
        var identity = req.GetRequiredApiIdentity();
        var effectiveOrgID = identity.EMAOrganizationID;
        var effectiveUserID = identity.UserID;

        if (effectiveOrgID <= 0)
        {
            return new BadRequestObjectResult(new { message = "Invalid organization ID." });
        }

        SendReferralsRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<SendReferralsRequest>(
                req.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize SendReferralsRequest for organization {OrgID}", effectiveOrgID);
            return new BadRequestObjectResult(new { message = "Invalid request payload format." });
        }

        if (request is null || request.Recipients.Count == 0)
        {
            return new BadRequestObjectResult(new { message = "At least one recipient is required." });
        }

        if (request.Recipients.Count > ReferralService.MaxRecipientsPerBatch)
        {
            return new BadRequestObjectResult(new
            {
                message = $"A single submission is limited to {ReferralService.MaxRecipientsPerBatch} recipients."
            });
        }

        try
        {
            var result = await _referralService.SendBatchReferralsAsync(
                effectiveOrgID,
                effectiveUserID,
                request,
                cancellationToken);

            return new OkObjectResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing batch referral emails for organization {OrgID}", effectiveOrgID);
            return new StatusCodeResult((int)HttpStatusCode.InternalServerError);
        }
    }
}
