using EMA.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace EMA_Api.Functions.Blobs;

public class BlobPut
{
    private readonly ILogger<BlobPut> _logger;
    private readonly IBlobStorageService _blobStorageService;

    public BlobPut(
        ILogger<BlobPut> logger,
        IBlobStorageService blobStorageService)
    {
        _logger = logger;
        _blobStorageService = blobStorageService;
    }

    [Function("BlobPut")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,             "put",
            Route = "blob/{blobID}")]
        HttpRequest req,
        string blobID)
    {
        try
        {
            if (req.Body is null || !req.Body.CanRead)
            {
                return new BadRequestObjectResult(new { message = "Request body is required." });
            }

            var identity =
                req.GetRequiredApiIdentity();

            var validatedUpload =
                await TenantBlobUploadValidator
                    .ValidateAsync(
                        req,
                        req.HttpContext
                            .RequestAborted);

            var tenantBlobID =
                TenantBlobName.ForOrganization(
                    identity.EMAOrganizationID,
                    blobID);

            await _blobStorageService.BlobPutAsync(
                blobID: tenantBlobID,
                content: validatedUpload.Content,
                contentType:
                    validatedUpload.ContentType,
                cancellationToken:
                    req.HttpContext
                        .RequestAborted);

            return new OkObjectResult(new
            {
                BlobID = blobID
            });
        }
        catch (ArgumentException ex)
        {
            return new BadRequestObjectResult(new { message = ex.Message });
        }
        catch (BlobUploadTooLargeException ex)
        {
            return new ObjectResult(
                new
                {
                    message =
                        ex.Message
                })
            {
                StatusCode =
                    StatusCodes
                        .Status413PayloadTooLarge
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error putting blob {BlobID}.", blobID);

            return new ObjectResult(new { message = "Error putting blob." })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }
}
