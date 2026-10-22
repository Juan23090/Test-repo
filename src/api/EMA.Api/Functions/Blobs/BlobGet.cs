using EMA.Api.Services;
using EMA.Api.Services.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace EMA_Api.Functions.Blobs;

public class BlobGet
{
    private readonly ILogger<BlobGet> _logger;
    private readonly IBlobStorageService _blobStorageService;
    private readonly ITenantBlobLegacyAuthorizationService
        _authorizationService;

    public BlobGet(
        ILogger<BlobGet> logger,
        IBlobStorageService blobStorageService,
        ITenantBlobLegacyAuthorizationService authorizationService)
    {
        _logger = logger;
        _blobStorageService = blobStorageService;
        _authorizationService = authorizationService;
    }

    [Function("BlobGet")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "blob/{blobID}")]
        HttpRequest req,
        string blobID)
    {
        try
        {
            var identity =
                req.GetRequiredApiIdentity();

            // Authorization happens before any blob content is opened. Possession
            // of an opaque blob ID is never treated as permission to read it.
            var authorization =
                await _authorizationService.AuthorizeReadAsync(
                    identity,
                    blobID,
                    req.HttpContext.RequestAborted);

            if (!authorization.IsAuthorized)
            {
                _logger.LogWarning(
                    "Denied blob {BlobID} for {ActorType} in organization {OrganizationID}.",
                    blobID,
                    identity.ActorType,
                    identity.EMAOrganizationID);

                return new ForbidResult();
            }

            var tenantBlobID =
                TenantBlobName.ForOrganization(
                    identity.EMAOrganizationID,
                    blobID);

            BlobGetResult blob;

            try
            {
                blob =
                    await _blobStorageService.BlobGetAsync(
                        tenantBlobID,
                        req.HttpContext.RequestAborted);
            }
            catch (FileNotFoundException)
                when (authorization.AllowSharedMenuItemMedia)
            {
                // Generated Task 36 recordings live in a dedicated shared
                // namespace. Access is still authorized from MenuItemResource
                // and the caller's category/role access before storage is read.
                blob =
                    await _blobStorageService.BlobGetAsync(
                        SharedMenuItemMediaBlobName.ForBlobID(blobID),
                        req.HttpContext.RequestAborted);
            }
            catch (FileNotFoundException)
                when (authorization.AllowLegacyFallback)
            {
                blob =
                    await _blobStorageService.BlobGetAsync(
                        blobID,
                        req.HttpContext.RequestAborted);
            }

            return new FileStreamResult(
                blob.Content,
                blob.ContentType)
            {
                FileDownloadName = blobID
            };
        }
        catch (ArgumentException ex)
        {
            return new BadRequestObjectResult(
                new { message = ex.Message });
        }
        catch (FileNotFoundException)
        {
            return new NotFoundObjectResult(
                new { message = "Blob was not found." });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error getting blob {BlobID}.",
                blobID);

            return new ObjectResult(
                new { message = "Error getting blob." })
            {
                StatusCode =
                    StatusCodes.Status500InternalServerError
            };
        }
    }
}
