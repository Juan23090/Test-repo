using EMA.Api.Services.ScreenFeedback;
using EMA.Api.Services.Security;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.ScreenFeedback;

public sealed class EnhancementRequestReviewFunction
{
    private readonly ILogger<EnhancementRequestReviewFunction> _logger;
    private readonly IScreenFeedbackRepository _repository;

    public EnhancementRequestReviewFunction(
        ILogger<EnhancementRequestReviewFunction> logger,
        IScreenFeedbackRepository repository)
    {
        _logger = logger;
        _repository = repository;
    }

    [Function("ReviewEnhancementRequests")]
    public async Task RunAsync(
        [TimerTrigger("0 0 2 * * *")]
        TimerInfo timerInfo,
        CancellationToken cancellationToken)
    {
        var automatedUserID =
            RequiredSecurityConfiguration.GetPositiveInt(
                "AUTOMATED_SERVICE_USER_ID");

        var updated =
            await _repository.ReviewExpiredEnhancementRequestsAsync(
                automatedUserID,
                cancellationToken);

        _logger.LogInformation(
            "Task 39 enhancement review completed. {UpdatedCount} request(s) moved from voting to their next status.",
            updated);
    }
}
