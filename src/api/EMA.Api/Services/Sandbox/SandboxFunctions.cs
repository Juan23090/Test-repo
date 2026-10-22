using System.Text.Json;
using EMA.Api.DTOs;
using EMA.Api.Services.Sandbox;
using EMA.Api.Services.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Sandbox;

public sealed class SandboxFunctions
{
    private const string CapabilityHeaderName =
        "X-Sandbox-Capability";

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true
        };

    private readonly ISandboxService _sandboxService;
    private readonly ISandboxCapabilityService
        _sandboxCapabilityService;
    private readonly IApiSessionService
        _apiSessionService;
    private readonly ILogger<SandboxFunctions> _logger;

    public SandboxFunctions(
        ISandboxService sandboxService,
        ISandboxCapabilityService sandboxCapabilityService,
        IApiSessionService apiSessionService,
        ILogger<SandboxFunctions> logger)
    {
        _sandboxService =
            sandboxService;

        _sandboxCapabilityService =
            sandboxCapabilityService;

        _apiSessionService =
            apiSessionService;

        _logger =
            logger;
    }

    [Function("GetCurrentSandbox")]
    public async Task<IActionResult> GetCurrentSandboxAsync(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,             "get",
            Route = "sandbox/current")]
        HttpRequest request)
    {
        var clientIPAddress =
            ClientIPAddressResolver
                .GetClientIPAddress(
                    request);

        try
        {
            var capabilityOrganizationID =
                await _sandboxCapabilityService
                    .ResolveOrganizationIDAsync(
                        GetCapability(
                            request),
                        request.HttpContext
                            .RequestAborted);

            SandboxWorkspaceSummaryDto? workspace =
                null;

            if (capabilityOrganizationID
                    .HasValue)
            {
                workspace =
                    await _sandboxService
                        .GetSandboxAsync(
                            capabilityOrganizationID
                                .Value,
                            request.HttpContext
                                .RequestAborted);
            }

            // A browser capability remains the preferred path. Sandbox
            // workspaces may also be reopened from another device at the
            // same location without replacing the original browser key.
            workspace ??=
                await _sandboxService
                    .GetCurrentSandboxAsync(
                        clientIPAddress,
                        request.HttpContext
                            .RequestAborted);

            if (workspace is null)
            {
                return NoCurrentSandbox();
            }

            return new OkObjectResult(
                new CurrentSandboxResponseDto
                {
                    HasActiveSandbox =
                        true,

                    Workspace =
                        workspace
                });
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error checking for the current sandbox.");

            return ServerError(
                "Unable to check for an existing sandbox.");
        }
    }

    [Function("CreateSandbox")]
    public async Task<IActionResult> CreateSandboxAsync(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,             "post",
            Route = "sandbox/create")]
        HttpRequest request)
    {
        var clientIPAddress =
            ClientIPAddressResolver
                .GetClientIPAddress(
                    request);

        try
        {
            var suppliedCapability =
                GetCapability(
                    request);

            var existingOrganizationID =
                await _sandboxCapabilityService
                    .ResolveOrganizationIDAsync(
                        suppliedCapability,
                        request.HttpContext
                            .RequestAborted);

            if (existingOrganizationID
                    .HasValue)
            {
                var existingWorkspace =
                    await _sandboxService
                        .GetSandboxAsync(
                            existingOrganizationID.Value,
                            request.HttpContext
                                .RequestAborted);

                if (existingWorkspace is not null)
                {
                    return new OkObjectResult(
                        new CreateSandboxResponseDto
                        {
                            Workspace =
                                existingWorkspace,

                            Capability =
                                suppliedCapability!
                        });
                }
            }

            var locationWorkspace =
                await _sandboxService
                    .GetCurrentSandboxAsync(
                        clientIPAddress,
                        request.HttpContext
                            .RequestAborted);

            if (locationWorkspace is not null)
            {
                return new OkObjectResult(
                    new CreateSandboxResponseDto
                    {
                        Workspace =
                            locationWorkspace,

                        // The same-location fallback does not replace the
                        // capability already held by another browser.
                        Capability =
                            string.Empty
                    });
            }

            var workspace =
                await _sandboxService
                    .CreateSandboxAsync(
                        clientIPAddress,
                        request.HttpContext.RequestAborted);

            var capability =
                await _sandboxCapabilityService
                    .CreateAsync(
                        workspace
                            .SandboxOrganizationID,
                        request.HttpContext
                            .RequestAborted);

            return new OkObjectResult(
                new CreateSandboxResponseDto
                {
                    Workspace =
                        workspace,

                    Capability =
                        capability
                });
        }
        catch (SandboxAlreadyExistsException)
        {
            return new ConflictObjectResult(
                new
                {
                    message =
                        "An active sandbox already exists for this location. Open it from the browser where it was created."
                });
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogError(
                exception,
                "Sandbox creation is not configured correctly.");

            return ServerError(
                "Unable to create the sandbox.");
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error creating a sandbox for client IP {ClientIPAddress}.",
                clientIPAddress);

            return ServerError(
                "Unable to create the sandbox.");
        }
    }

    [Function("ResumeSandbox")]
    public async Task<IActionResult> ResumeSandboxAsync(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,             "post",
            Route = "sandbox/resume")]
        HttpRequest request)
    {
        ResumeSandboxRequestDto? resumeRequest;

        try
        {
            resumeRequest =
                await JsonSerializer
                    .DeserializeAsync<ResumeSandboxRequestDto>(
                        request.Body,
                        JsonOptions,
                        request.HttpContext.RequestAborted);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult(
                new
                {
                    message =
                        "Invalid sandbox resume request."
                });
        }

        if (resumeRequest is null ||
            resumeRequest.SandboxOrganizationID <= 0)
        {
            return new BadRequestObjectResult(
                new
                {
                    message =
                        "A sandbox organization is required."
                });
        }

        var clientIPAddress =
            ClientIPAddressResolver
                .GetClientIPAddress(
                    request);

        try
        {
            var capabilityOrganizationID =
                await _sandboxCapabilityService
                    .ResolveOrganizationIDAsync(
                        GetCapability(
                            request),
                        request.HttpContext
                            .RequestAborted);

            var isCapabilityAuthorized =
                capabilityOrganizationID ==
                resumeRequest
                    .SandboxOrganizationID;

            if (!isCapabilityAuthorized)
            {
                var locationWorkspace =
                    await _sandboxService
                        .GetCurrentSandboxAsync(
                            clientIPAddress,
                            request.HttpContext
                                .RequestAborted);

                if (locationWorkspace?
                        .SandboxOrganizationID !=
                    resumeRequest
                        .SandboxOrganizationID)
                {
                    return new ObjectResult(
                        new
                        {
                            message =
                                "A valid sandbox capability or matching sandbox location is required."
                        })
                    {
                        StatusCode =
                            StatusCodes.Status403Forbidden
                    };
                }
            }

            var loginResponse =
                await _sandboxService
                    .ResumeSandboxAsync(
                        resumeRequest.SandboxOrganizationID,
                        clientIPAddress,
                        request.HttpContext.RequestAborted);

            if (loginResponse is null)
            {
                return new NotFoundObjectResult(
                    new
                    {
                        message =
                            "The sandbox could not be resumed."
                    });
            }

            await AddAccessTokenAsync(
                loginResponse,
                request.HttpContext
                    .RequestAborted);

            return new OkObjectResult(
                loginResponse);
        }
        catch (UnauthorizedAccessException exception)
        {
            return new ObjectResult(
                new
                {
                    message =
                        exception.Message
                })
            {
                StatusCode =
                    StatusCodes.Status403Forbidden
            };
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error resuming Sandbox OrganizationID {OrganizationID}.",
                resumeRequest.SandboxOrganizationID);

            return ServerError(
                "Unable to resume the sandbox.");
        }
    }

    [Function("ResumeSandboxByPhone")]
    public async Task<IActionResult> ResumeSandboxByPhoneAsync(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,             "post",
            Route = "sandbox/resume-by-phone")]
        HttpRequest request)
    {
        ResumeSandboxByPhoneRequestDto? resumeRequest;

        try
        {
            resumeRequest =
                await JsonSerializer
                    .DeserializeAsync<ResumeSandboxByPhoneRequestDto>(
                        request.Body,
                        JsonOptions,
                        request.HttpContext.RequestAborted);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult(
                new
                {
                    message =
                        "Invalid sandbox phone request."
                });
        }

        if (resumeRequest is null ||
            string.IsNullOrWhiteSpace(
                resumeRequest.PhoneNumber))
        {
            return new BadRequestObjectResult(
                new
                {
                    message =
                        "A sandbox phone number is required."
                });
        }

        var clientIPAddress =
            ClientIPAddressResolver
                .GetClientIPAddress(
                    request);

        try
        {
            var loginResponse =
                await _sandboxService
                    .ResumeSandboxByPhoneAsync(
                        resumeRequest.PhoneNumber,
                        clientIPAddress,
                        request.HttpContext.RequestAborted);

            if (loginResponse is null)
            {
                return new NotFoundObjectResult(
                    new
                    {
                        message =
                            "That phone number is not connected to an active sandbox."
                    });
            }

            await AddAccessTokenAsync(
                loginResponse,
                request.HttpContext
                    .RequestAborted);

            return new OkObjectResult(
                loginResponse);
        }
        catch (ArgumentException exception)
        {
            return new BadRequestObjectResult(
                new
                {
                    message =
                        exception.Message
                });
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error resuming a sandbox by phone.");

            return ServerError(
                "Unable to resume the sandbox.");
        }
    }

    [Function("GetSandboxTestEmail")]
    public async Task<IActionResult> GetSandboxTestEmailAsync(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,             "get",
            Route = "sandbox/{sandboxOrganizationID:int}/test-email")]
        HttpRequest request,
        int sandboxOrganizationID)
    {
        var clientIPAddress =
            ClientIPAddressResolver
                .GetClientIPAddress(
                    request);

        try
        {
            if (!await IsSandboxRequestAuthorizedAsync(
                    request,
                    sandboxOrganizationID))
            {
                return new ObjectResult(
                    new
                    {
                        message =
                            "This browser is not authorized to use that sandbox."
                    })
                {
                    StatusCode =
                        StatusCodes.Status403Forbidden
                };
            }

            var draft =
                await _sandboxService
                    .GetTestEmailDraftAsync(
                        sandboxOrganizationID,
                        clientIPAddress,
                        request.HttpContext.RequestAborted);

            if (draft is null)
            {
                return new NotFoundObjectResult(
                    new
                    {
                        message =
                            "The sandbox test email could not be loaded."
                    });
            }

            return new OkObjectResult(
                draft);
        }
        catch (UnauthorizedAccessException exception)
        {
            return new ObjectResult(
                new
                {
                    message =
                        exception.Message
                })
            {
                StatusCode =
                    StatusCodes.Status403Forbidden
            };
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error loading the sandbox test email for OrganizationID {OrganizationID}.",
                sandboxOrganizationID);

            return ServerError(
                "Unable to load the sandbox test email.");
        }
    }

    [Function("SendSandboxTestEmail")]
    public async Task<IActionResult> SendSandboxTestEmailAsync(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,             "post",
            Route = "sandbox/{sandboxOrganizationID:int}/test-email/send")]
        HttpRequest request,
        int sandboxOrganizationID)
    {
        SendSandboxTestEmailRequestDto? emailRequest;

        try
        {
            emailRequest =
                await JsonSerializer
                    .DeserializeAsync<SendSandboxTestEmailRequestDto>(
                        request.Body,
                        JsonOptions,
                        request.HttpContext.RequestAborted);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult(
                new
                {
                    message =
                        "Invalid sandbox test email request."
                });
        }

        if (emailRequest is null)
        {
            return new BadRequestObjectResult(
                new
                {
                    message =
                        "Sandbox test email information is required."
                });
        }

        if (emailRequest.SandboxOrganizationID > 0 &&
            emailRequest.SandboxOrganizationID !=
            sandboxOrganizationID)
        {
            return new BadRequestObjectResult(
                new
                {
                    message =
                        "The sandbox organization in the route and request do not match."
                });
        }

        emailRequest.SandboxOrganizationID =
            sandboxOrganizationID;

        var clientIPAddress =
            ClientIPAddressResolver
                .GetClientIPAddress(
                    request);

        try
        {
            if (!await IsSandboxRequestAuthorizedAsync(
                    request,
                    sandboxOrganizationID))
            {
                return new ObjectResult(
                    new
                    {
                        message =
                            "This browser is not authorized to use that sandbox."
                    })
                {
                    StatusCode =
                        StatusCodes.Status403Forbidden
                };
            }

            var response =
                await _sandboxService
                    .SendTestEmailAsync(
                        emailRequest,
                        clientIPAddress,
                        request.HttpContext.RequestAborted);

            return new OkObjectResult(
                response);
        }
        catch (ArgumentException exception)
        {
            return new BadRequestObjectResult(
                new
                {
                    message =
                        exception.Message
                });
        }
        catch (UnauthorizedAccessException exception)
        {
            return new ObjectResult(
                new
                {
                    message =
                        exception.Message
                })
            {
                StatusCode =
                    StatusCodes.Status403Forbidden
            };
        }
        catch (InvalidOperationException exception)
        {
            return new ObjectResult(
                new
                {
                    message =
                        exception.Message
                })
            {
                StatusCode =
                    StatusCodes.Status409Conflict
            };
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error sending the sandbox test email for OrganizationID {OrganizationID}.",
                sandboxOrganizationID);

            return ServerError(
                "Unable to send the sandbox test email.");
        }
    }

    private static ObjectResult ServerError(
        string message)
    {
        return new ObjectResult(
            new
            {
                message
            })
        {
            StatusCode =
                StatusCodes.Status500InternalServerError
        };
    }

    private static NotFoundObjectResult
        NoCurrentSandbox()
    {
        return new NotFoundObjectResult(
            new
            {
                message =
                    "No active sandbox is available in this browser."
            });
    }

    private async Task<bool> IsSandboxRequestAuthorizedAsync(
        HttpRequest request,
        int sandboxOrganizationID)
    {
        var capabilityOrganizationID =
            await _sandboxCapabilityService
                .ResolveOrganizationIDAsync(
                    GetCapability(
                        request),
                    request.HttpContext
                        .RequestAborted);

        if (capabilityOrganizationID ==
            sandboxOrganizationID)
        {
            return true;
        }

        try
        {
            var identity =
                request.GetRequiredApiIdentity();

            return identity.EMAOrganizationID ==
                   sandboxOrganizationID;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? GetCapability(
        HttpRequest request)
    {
        var value =
            request.Headers[
                    CapabilityHeaderName]
                .ToString()
                .Trim();

        return string.IsNullOrWhiteSpace(
                value)
            ? null
            : value;
    }

    private async Task AddAccessTokenAsync(
        LoginResponseDto loginResponse,
        CancellationToken cancellationToken)
    {
        var accessToken =
            await _apiSessionService
                .CreateSessionAsync(
                    loginResponse,
                    cancellationToken);

        loginResponse.AccessToken =
            accessToken.Token;

        loginResponse
            .AccessTokenExpiresAtUtc =
                accessToken.ExpiresAtUtc;
    }
}
