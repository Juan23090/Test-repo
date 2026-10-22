using System.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Dashboard;

/// <summary>
/// GET /api/dashboard/orgsettings/{orgID}
/// Returns the organization feature flags used by the dashboard.
/// </summary>
public sealed class GetDashboardOrgSettings
{
    private readonly ILogger<GetDashboardOrgSettings>
        _logger;

    private readonly string _connectionString;

    public GetDashboardOrgSettings(
        ILogger<GetDashboardOrgSettings> logger)
    {
        _logger = logger;

        _connectionString =
            Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString.");
    }

    [Function("GetDashboardOrgSettings")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
                        Route = "dashboard/orgsettings/{orgID:int}")]
        HttpRequest request,
        int orgID)
    {
        _logger.LogInformation(
            "GetDashboardOrgSettings triggered for org {OrgID}.",
            orgID);

        if (orgID <= 0)
        {
            return new BadRequestObjectResult(new
            {
                message =
                    "A valid organization ID is required."
            });
        }

        try
        {
            await using var connection =
                new SqlConnection(
                    _connectionString);

            await connection.OpenAsync();

            /*
             * dbo.Organization currently has no
             * UseCurrencyTypes column.
             *
             * Return false until the database field
             * is added instead of allowing the whole
             * dashboard request to fail.
             */
            const string sql = """
                SELECT
                    UseCampuses,
                    CAST(0 AS bit) AS UseCurrencyTypes,
                    UseFunds,
                    UseMethodsOfGiving
                FROM dbo.Organization
                WHERE EMAOrganizationID = @OrgID;
                """;

            await using var command =
                new SqlCommand(
                    sql,
                    connection);

            command.Parameters.Add(
                "@OrgID",
                SqlDbType.Int)
                .Value = orgID;

            await using var reader =
                await command.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                return new NotFoundObjectResult(new
                {
                    message =
                        $"Organization {orgID} was not found."
                });
            }

            return new OkObjectResult(new
            {
                UseCampuses =
                    ParseBooleanFlag(
                        reader,
                        0),

                UseCurrencyTypes =
                    ParseBooleanFlag(
                        reader,
                        1),

                UseFunds =
                    ParseBooleanFlag(
                        reader,
                        2),

                UseMethodsOfGiving =
                    ParseBooleanFlag(
                        reader,
                        3)
            });
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error fetching dashboard organization settings for EMAOrganizationID {OrgID}.",
                orgID);

            return new ObjectResult(new
            {
                message =
                    "Unable to load dashboard organization settings."
            })
            {
                StatusCode =
                    StatusCodes
                        .Status500InternalServerError
            };
        }
    }

    private static bool ParseBooleanFlag(
        SqlDataReader reader,
        int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return false;
        }

        var value =
            reader.GetValue(ordinal);

        if (value is bool booleanValue)
        {
            return booleanValue;
        }

        if (value is byte byteValue)
        {
            return byteValue != 0;
        }

        if (value is short shortValue)
        {
            return shortValue != 0;
        }

        if (value is int intValue)
        {
            return intValue != 0;
        }

        var raw =
            value.ToString()
                ?.Trim()
            ?? string.Empty;

        return raw.Equals(
                   "Y",
                   StringComparison.OrdinalIgnoreCase)
               ||
               raw.Equals(
                   "YES",
                   StringComparison.OrdinalIgnoreCase)
               ||
               raw.Equals(
                   "TRUE",
                   StringComparison.OrdinalIgnoreCase)
               ||
               raw.Equals(
                   "T",
                   StringComparison.OrdinalIgnoreCase)
               ||
               raw.Equals(
                   "1",
                   StringComparison.OrdinalIgnoreCase);
    }

}
