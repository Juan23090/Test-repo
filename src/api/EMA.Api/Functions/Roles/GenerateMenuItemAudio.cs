using EMA.Api.Services;
using EMA.Api.Services.Audio;
using EMA.Api.Services.Navigation;
using EMA.Api.Services.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Roles;

public sealed class GenerateMenuItemAudio
{
    private readonly ILogger<GenerateMenuItemAudio> _logger;
    private readonly IMenuItemAudioGenerationPersistence _persistence;
    private readonly ITextToSpeechService _textToSpeech;
    private readonly IBlobStorageService _blobStorage;

    public GenerateMenuItemAudio(
        ILogger<GenerateMenuItemAudio> logger,
        IMenuItemAudioGenerationPersistence persistence,
        ITextToSpeechService textToSpeech,
        IBlobStorageService blobStorage)
    {
        _logger = logger;
        _persistence = persistence;
        _textToSpeech = textToSpeech;
        _blobStorage = blobStorage;
    }

    [Function("GenerateMenuItemAudio")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "post",
            Route = "menu-items/{menuItemID:int}/resources/{menuItemRowID:int}/generate-audio")]
        HttpRequest request,
        int menuItemID,
        int menuItemRowID)
    {
        if (menuItemID <= 0 || menuItemRowID <= 0)
        {
            return new BadRequestObjectResult(new
            {
                message = "A valid MenuItem and saved Audio resource are required."
            });
        }

        var cancellationToken = request.HttpContext.RequestAborted;

        try
        {
            if (!await _persistence.TableExistsAsync(cancellationToken))
            {
                return new ObjectResult(new
                {
                    message = "MenuItem resource data is unavailable, so a recording cannot be generated."
                })
                {
                    StatusCode = StatusCodes.Status409Conflict
                };
            }

            var original =
                await _persistence.GetAudioResourceAsync(
                    menuItemID,
                    menuItemRowID,
                    cancellationToken);

            if (original is null)
            {
                return new NotFoundObjectResult(new
                {
                    message = "The selected MenuItem resource was not found. Save the resource and try again."
                });
            }

            if (!original.MenuItemResourceTypeID.Equals(
                    "A",
                    StringComparison.OrdinalIgnoreCase))
            {
                return new BadRequestObjectResult(new
                {
                    message = "Production recordings can only be generated for Audio resources."
                });
            }

            var audioScript = original.AudioScript?.Trim();

            if (string.IsNullOrWhiteSpace(audioScript))
            {
                return new BadRequestObjectResult(new
                {
                    message = "Enter and save an Audio Script before generating a recording."
                });
            }

            GeneratedSpeechAudio generated;

            try
            {
                generated =
                    await _textToSpeech.GenerateMp3Async(
                        audioScript,
                        cancellationToken);
            }
            catch (ArgumentException exception)
            {
                return new BadRequestObjectResult(new
                {
                    message = exception.Message
                });
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Google Cloud Text-to-Speech failed for MenuItem {MenuItemID}, resource {MenuItemRowID}.",
                    menuItemID,
                    menuItemRowID);

                return new ObjectResult(new
                {
                    message = "Google Cloud Text-to-Speech is unavailable. Check the API credentials/configuration and try again."
                })
                {
                    StatusCode = StatusCodes.Status502BadGateway
                };
            }

            if (generated.Content is null || generated.Content.Length == 0)
            {
                return new ObjectResult(new
                {
                    message = "Google Cloud Text-to-Speech returned an empty recording."
                })
                {
                    StatusCode = StatusCodes.Status502BadGateway
                };
            }

            // Re-read after the provider call. A user may have edited/saved the
            // resource while Google was synthesizing the recording.
            var latest =
                await _persistence.GetAudioResourceAsync(
                    menuItemID,
                    menuItemRowID,
                    cancellationToken);

            if (!MatchesOriginalAudioResource(original, latest))
            {
                return new ObjectResult(new
                {
                    message = "The Audio resource changed while the recording was being generated. Save it and try again."
                })
                {
                    StatusCode = StatusCodes.Status409Conflict
                };
            }

            var newBlobID = Guid.NewGuid().ToString("D");
            var sharedBlobName =
                SharedMenuItemMediaBlobName.ForBlobID(newBlobID);

            try
            {
                await using var stream =
                    new MemoryStream(
                        generated.Content,
                        writable: false);

                await _blobStorage.BlobPutAsync(
                    sharedBlobName,
                    stream,
                    string.IsNullOrWhiteSpace(generated.ContentType)
                        ? "audio/mpeg"
                        : generated.ContentType,
                    cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Unable to store generated MenuItem audio {BlobID}.",
                    newBlobID);

                return new ObjectResult(new
                {
                    message = "The recording was generated but could not be saved to Blob storage."
                })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }

            bool updated;

            try
            {
                updated =
                    await _persistence.TryUpdateGeneratedBlobAsync(
                        menuItemID,
                        menuItemRowID,
                        audioScript,
                        original.AudioVideoBlob,
                        newBlobID,
                        cancellationToken);
            }
            catch (Exception exception)
            {
                await TryDeleteSharedBlobAsync(
                    sharedBlobName,
                    cancellationToken);

                _logger.LogError(
                    exception,
                    "Unable to update MenuItem {MenuItemID}, resource {MenuItemRowID} with generated audio {BlobID}.",
                    menuItemID,
                    menuItemRowID,
                    newBlobID);

                return new StatusCodeResult(
                    StatusCodes.Status500InternalServerError);
            }

            if (!updated)
            {
                await TryDeleteSharedBlobAsync(
                    sharedBlobName,
                    cancellationToken);

                return new ObjectResult(new
                {
                    message = "The Audio resource changed before the recording could be saved. The existing recording was left unchanged."
                })
                {
                    StatusCode = StatusCodes.Status409Conflict
                };
            }

            if (!string.IsNullOrWhiteSpace(original.AudioVideoBlob))
            {
                await TryDeleteSharedBlobAsync(
                    SharedMenuItemMediaBlobName.ForBlobID(
                        original.AudioVideoBlob),
                    cancellationToken);
            }

            _logger.LogInformation(
                "Generated production audio for MenuItem {MenuItemID}, resource {MenuItemRowID}, blob {BlobID}.",
                menuItemID,
                menuItemRowID,
                newBlobID);

            return new OkObjectResult(new
            {
                BlobID = newBlobID,
                MenuItemID = menuItemID,
                MenuItemRowID = menuItemRowID
            });
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error generating production audio for MenuItem {MenuItemID}, resource {MenuItemRowID}.",
                menuItemID,
                menuItemRowID);

            return new StatusCodeResult(
                StatusCodes.Status500InternalServerError);
        }
    }

    private async Task TryDeleteSharedBlobAsync(
        string storageBlobID,
        CancellationToken cancellationToken)
    {
        try
        {
            await _blobStorage.BlobDeleteAsync(
                storageBlobID,
                cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Unable to clean up shared MenuItem media blob {BlobID}.",
                storageBlobID);
        }
    }

    private static bool MatchesOriginalAudioResource(
        MenuItemAudioResourceSnapshot original,
        MenuItemAudioResourceSnapshot? latest)
    {
        return latest is not null &&
               latest.MenuItemID == original.MenuItemID &&
               latest.MenuItemRowID == original.MenuItemRowID &&
               latest.MenuItemResourceTypeID.Equals(
                   original.MenuItemResourceTypeID,
                   StringComparison.OrdinalIgnoreCase) &&
               string.Equals(
                   latest.AudioScript?.Trim(),
                   original.AudioScript?.Trim(),
                   StringComparison.Ordinal) &&
               string.Equals(
                   latest.AudioVideoBlob?.Trim(),
                   original.AudioVideoBlob?.Trim(),
                   StringComparison.Ordinal);
    }
}
