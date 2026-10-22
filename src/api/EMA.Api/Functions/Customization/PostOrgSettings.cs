using System.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA_Api.Functions.Customization;

public class PostOrgSettings
{
    private readonly ILogger<PostOrgSettings> _logger;
    private readonly string _connectionString;

    public PostOrgSettings(ILogger<PostOrgSettings> logger)
    {
        _logger = logger;
        _connectionString =
            Environment.GetEnvironmentVariable("SqlConnectionString")
            ?? throw new InvalidOperationException("Missing SqlConnectionString.");
    }

    [Function("PostOrgSettings")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "post",
            Route = "customization/settings/{orgID:int}")]
        HttpRequest req,
        int orgID)
    {
        _logger.LogInformation(
            "PostOrgSettings triggered for org {OrgID}.",
            orgID);

        try
        {
            var identity = req.GetRequiredApiIdentity();

            if (identity.EMAOrganizationID != orgID)
            {
                return new ForbidResult();
            }

            using var document =
                await System.Text.Json.JsonDocument.ParseAsync(
                    req.Body,
                    cancellationToken: req.HttpContext.RequestAborted);

            var root = document.RootElement;

            const string sql = """
                UPDATE dbo.Organization
                SET UseCampuses = @UseCampuses,
                    UseEvents = @UseEvents,
                    UseFunds = @UseFunds,
                    UseMethodsOfGiving = @UseMethodsOfGiving,
                    UseSources = @UseSources,
                    UseTributeTypes = @UseTributeTypes,
                    UseAnonymous = @UseAnonymous,
                    DateTimeUpdated = SYSUTCDATETIME(),
                    UpdatedByUserID = @UpdatedByUserID
                WHERE EMAOrganizationID = @EMAOrganizationID;
                """;

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(req.HttpContext.RequestAborted);

            await using var command = new SqlCommand(sql, connection);
            AddFlag(command, "@UseCampuses", root, "UseCampuses", defaultValue: 'Y');
            AddFlag(command, "@UseEvents", root, "UseEvents", defaultValue: 'Y');
            AddFlag(command, "@UseFunds", root, "UseFunds", defaultValue: 'Y');
            AddFlag(command, "@UseMethodsOfGiving", root, "UseMethodsOfGiving", defaultValue: 'Y');
            AddFlag(command, "@UseSources", root, "UseSources", defaultValue: 'Y');
            AddFlag(command, "@UseTributeTypes", root, "UseTributeTypes", defaultValue: 'Y');
            AddFlag(command, "@UseAnonymous", root, "UseAnonymous", defaultValue: 'N');

            command.Parameters.Add("@EMAOrganizationID", SqlDbType.Int).Value = orgID;
            command.Parameters.Add("@UpdatedByUserID", SqlDbType.Int).Value = identity.UserID;

            var affected =
                await command.ExecuteNonQueryAsync(
                    req.HttpContext.RequestAborted);

            return affected == 0
                ? new NotFoundResult()
                : new OkResult();
        }
        catch (UnauthorizedAccessException)
        {
            return new UnauthorizedResult();
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error updating org settings for organization {OrgID}.",
                orgID);

            return new StatusCodeResult(
                StatusCodes.Status500InternalServerError);
        }
    }

    private static void AddFlag(
        SqlCommand command,
        string parameterName,
        System.Text.Json.JsonElement root,
        string propertyName,
        char defaultValue)
    {
        var value = defaultValue;

        if (root.TryGetProperty(propertyName, out var property))
        {
            value = property.ValueKind switch
            {
                System.Text.Json.JsonValueKind.True => 'Y',
                System.Text.Json.JsonValueKind.False => 'N',
                System.Text.Json.JsonValueKind.Number when
                    property.TryGetInt32(out var number) && number == 1 => 'Y',
                System.Text.Json.JsonValueKind.String when
                    IsEnabled(property.GetString()) => 'Y',
                _ => 'N'
            };
        }

        command.Parameters
            .Add(parameterName, SqlDbType.Char, 1)
            .Value = value.ToString();
    }

    private static bool IsEnabled(string? value) =>
        string.Equals(value?.Trim(), "Y", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value?.Trim(), "1", StringComparison.OrdinalIgnoreCase);
}
