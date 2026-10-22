using EMA.Api.Services.Navigation;
using EMA.Api.Services.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.SetupWizard;

public sealed class GetSetupWizard
{
    private readonly ILogger<GetSetupWizard> _logger;
    private readonly ISetupWizardQuery _query;
    private readonly ApiRequestBoundaryValidator _authorization;

    public GetSetupWizard(
        ILogger<GetSetupWizard> logger,
        ISetupWizardQuery query,
        ApiRequestBoundaryValidator authorization)
    {
        _logger = logger;
        _query = query;
        _authorization = authorization;
    }

    [Function("GetSetupWizard")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "organization/{orgID:int}/setup-wizard")]
        HttpRequest request,
        int orgID)
    {
        var identity =
            request.GetRequiredApiIdentity();

        _logger.LogInformation(
            "GetSetupWizard triggered for org {OrgID}, user {UserID}.",
            orgID,
            identity.UserID);

        try
        {
            var wizard =
                await _query.GetForOrganizationUserAsync(
                    orgID,
                    identity.UserID,
                    _authorization.IsOrganizationAdministrator(
                        identity),
                    request.HttpContext.RequestAborted);

            return wizard is null
                ? new NotFoundObjectResult(
                    new
                    {
                        message =
                            "The organization was not found."
                    })
                : new OkObjectResult(
                    wizard);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Unable to load Setup Wizard for org {OrgID}, user {UserID}.",
                orgID,
                identity.UserID);

            return new ObjectResult(
                new
                {
                    message =
                        "Unable to load the Setup Wizard."
                })
            {
                StatusCode =
                    StatusCodes.Status500InternalServerError
            };
        }
    }
}
