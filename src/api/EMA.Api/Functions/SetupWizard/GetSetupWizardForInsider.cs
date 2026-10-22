using EMA.Api.Services.Navigation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.SetupWizard;

public sealed class GetSetupWizardForInsider
{
    private readonly ILogger<GetSetupWizardForInsider> _logger;
    private readonly ISetupWizardQuery _query;

    public GetSetupWizardForInsider(
        ILogger<GetSetupWizardForInsider> logger,
        ISetupWizardQuery query)
    {
        _logger = logger;
        _query = query;
    }

    [Function("GetSetupWizardForInsider")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "platform/setup-wizard/organizations/{orgID:int}")]
        HttpRequest request,
        int orgID)
    {
        _logger.LogInformation(
            "GetSetupWizardForInsider triggered for target org {OrgID}.",
            orgID);

        try
        {
            var wizard =
                await _query.GetForPlatformAdministratorAsync(
                    orgID,
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
                "Unable to load insider Setup Wizard for target org {OrgID}.",
                orgID);

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
