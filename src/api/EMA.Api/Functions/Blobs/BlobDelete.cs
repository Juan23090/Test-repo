using EMA.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace EMA_Api.Functions.Blobs;

public class BlobDelete
{
    private readonly ILogger<BlobDelete> _logger;
    private readonly IBlobStorageService _blobStorageService;

    public BlobDelete(
        ILogger<BlobDelete> logger,
        IBlobStorageService blobStorageService)
    {
        _logger = logger;
        _blobStorageService = blobStorageService;
    }

    [Function("BlobDelete")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,             "delete",
            Route = "blob/{blobID}")]
        HttpRequest req,
        string blobID)
    {
        try
        {
            var identity =
                req.GetRequiredApiIdentity();

            var tenantBlobID =
                TenantBlobName.ForOrganization(
                    identity.EMAOrganizationID,
                    blobID);

            var deleted =
                await _blobStorageService
                    .BlobDeleteAsync(
                        tenantBlobID,
                        req.HttpContext
                            .RequestAborted);

            if (!deleted)
            {
                return new NotFoundObjectResult(new { message = "Blob was not found." });
            }

            return new NoContentResult();
        }
        catch (ArgumentException ex)
        {
            return new BadRequestObjectResult(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting blob {BlobID}.", blobID);

            return new ObjectResult(new { message = "Error deleting blob." })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }
}
