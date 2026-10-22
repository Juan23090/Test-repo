using System.Net;
using EMA.Api.Services.Referrals;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Referrals;

public sealed class GetReferralHistory
{
    private readonly IReferralService _referralService;
    private readonly ILogger<GetReferralHistory> _logger;

    public GetReferralHistory(
        IReferralService referralService,
        ILogger<GetReferralHistory> logger)
    {
        _referralService = referralService;
        _logger = logger;
    }

    [Function("GetReferralHistory")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "organization/{orgID:int}/referrals/history")]
        HttpRequest req,
        int orgID,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Retrieving referral history for organization {OrgID}", orgID);

        if (orgID <= 0)
        {
            return new BadRequestObjectResult(new { message = "Invalid organization ID." });
        }

        try
        {
            var summary = await _referralService.GetReferralHistoryAsync(orgID, cancellationToken);
            return new OkObjectResult(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching referral history for organization {OrgID}", orgID);
            return new StatusCodeResult((int)HttpStatusCode.InternalServerError);
        }
    }
}
