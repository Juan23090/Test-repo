using System.Data;
using System.Net;
using EMA.Api.Services.Security;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Dashboard;

public sealed class GetDonorDashboardSettings
{
    private readonly ILogger<GetDonorDashboardSettings>
        _logger;

    private readonly string _connectionString;

    public GetDonorDashboardSettings(
        ILogger<GetDonorDashboardSettings> logger)
    {
        _logger =
            logger;

        _connectionString =
            Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString.");
    }

    [Function("GetDonorDashboardSettings")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "dashboard/donor-settings/{orgID:int}")]
        HttpRequestData req,
        int orgID)
    {
        try
        {
            var identity =
                req.GetRequiredApiIdentity();

            if (identity.ActorType !=
                    ApiActorType.Donor ||
                identity.EMAOrganizationID !=
                    orgID ||
                !identity.DonorID.HasValue)
            {
                return await CreateErrorResponseAsync(
                    req,
                    HttpStatusCode.Forbidden,
                    "This donor session cannot access the requested organization.");
            }

            await using var connection =
                new SqlConnection(
                    _connectionString);

            await connection.OpenAsync(
                req.FunctionContext.CancellationToken);

            const string sql = """
                SELECT
                    ISNULL(KPIDefaultHistoryDays, 7),
                    KPIGraphDonorLandingPage
                FROM dbo.Organization
                WHERE EMAOrganizationID = @EMAOrganizationID;
                """;

            await using var command =
                new SqlCommand(
                    sql,
                    connection);

            command.Parameters
                .Add(
                    "@EMAOrganizationID",
                    SqlDbType.Int)
                .Value =
                    identity.EMAOrganizationID;

            await using var reader =
                await command.ExecuteReaderAsync(
                    req.FunctionContext.CancellationToken);

            if (!await reader.ReadAsync(
                    req.FunctionContext.CancellationToken))
            {
                return await CreateErrorResponseAsync(
                    req,
                    HttpStatusCode.NotFound,
                    "Organization not found.");
            }

            var days =
                reader.IsDBNull(0)
                    ? 7
                    : Math.Clamp(
                        Convert.ToInt32(
                            reader.GetValue(0)),
                        1,
                        3650);

            var graph =
                reader.IsDBNull(1)
                    ? "V"
                    : reader
                        .GetValue(1)
                        ?.ToString()
                        ?.Trim()
                        .ToUpperInvariant();

            if (graph is not ("F" or "M" or "V"))
            {
                graph =
                    "V";
            }

            var response =
                req.CreateResponse(
                    HttpStatusCode.OK);

            await response.WriteAsJsonAsync(new
            {
                Days =
                    days,

                Graph =
                    graph
            });

            return response;
        }
        catch (UnauthorizedAccessException)
        {
            return await CreateErrorResponseAsync(
                req,
                HttpStatusCode.Unauthorized,
                "An authenticated donor session is required.");
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error fetching donor dashboard settings for organization {OrgID}.",
                orgID);

            return await CreateErrorResponseAsync(
                req,
                HttpStatusCode.InternalServerError,
                "Unable to load donor dashboard settings.");
        }
    }

    private static async Task<HttpResponseData>
        CreateErrorResponseAsync(
            HttpRequestData req,
            HttpStatusCode statusCode,
            string message)
    {
        var response =
            req.CreateResponse(
                statusCode);

        await response.WriteAsJsonAsync(new
        {
            message
        });

        return response;
    }
}
