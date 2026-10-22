using System.Net;
using EMA.Api.Services.Referrals;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Referrals;

public sealed class GetReferralEligibility
{
    private readonly IReferralService _referralService;
    private readonly ILogger<GetReferralEligibility> _logger;

    public GetReferralEligibility(
        IReferralService referralService,
        ILogger<GetReferralEligibility> logger)
    {
        _referralService = referralService;
        _logger = logger;
    }

    [Function("GetReferralEligibility")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "organization/{orgID:int}/referrals/eligibility")]
        HttpRequest req,
        int orgID,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Checking referral eligibility for organization {OrgID}", orgID);

        if (orgID <= 0)
        {
            return new BadRequestObjectResult(new { message = "Invalid organization ID." });
        }

        try
        {
            var eligibility = await _referralService.GetEligibilityAsync(orgID, cancellationToken);
            return new OkObjectResult(eligibility);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check referral eligibility for organization {OrgID}", orgID);
            return new StatusCodeResult((int)HttpStatusCode.InternalServerError);
        }
    }
}
