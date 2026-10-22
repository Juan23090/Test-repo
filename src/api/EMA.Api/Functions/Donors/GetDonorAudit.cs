using System.Data;
using System.Net;
using EMA.Api.Services.Security;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Donors;

public sealed class GetDonorAudit
{
    private readonly ILogger<GetDonorAudit> _logger;
    private readonly string _connectionString;

    public GetDonorAudit(
        ILogger<GetDonorAudit> logger)
    {
        _logger =
            logger;

        _connectionString =
            Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString.");
    }

    [Function("GetDonorAudit")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "donors/{orgID:int}/{donorID:int}/audit")]
        HttpRequestData request,
        int orgID,
        int donorID)
    {
        var identity =
            request.GetRequiredApiIdentity();

        if (identity.EMAOrganizationID != orgID)
        {
            var forbidden =
                request.CreateResponse(
                    HttpStatusCode.Forbidden);

            await forbidden.WriteAsJsonAsync(new
            {
                message =
                    "The donor does not belong to the active organization."
            });

            return forbidden;
        }

        try
        {
            var audits =
                new List<object>();

            await using var connection =
                new SqlConnection(
                    _connectionString);

            await connection.OpenAsync(
                request.FunctionContext
                    .CancellationToken);

            const string sql = """
                SELECT
                    audit.DateTimeCreated,
                    audit.ChangedByUserID,
                    audit.DonorAuditPayload
                FROM dbo.DonorAudit AS audit
                INNER JOIN dbo.Donor AS donor
                    ON donor.EMADonorID = audit.EMADonorID
                   AND donor.EMAOrganizationID = audit.EMAOrganizationID
                WHERE audit.EMAOrganizationID = @EMAOrganizationID
                  AND audit.EMADonorID = @EMADonorID
                ORDER BY
                    audit.DateTimeCreated DESC,
                    audit.DonorAuditID DESC;
                """;

            await using var command =
                new SqlCommand(
                    sql,
                    connection);

            command.Parameters.Add(
                "@EMAOrganizationID",
                SqlDbType.Int)
                .Value =
                    orgID;

            command.Parameters.Add(
                "@EMADonorID",
                SqlDbType.Int)
                .Value =
                    donorID;

            await using var reader =
                await command.ExecuteReaderAsync(
                    request.FunctionContext
                        .CancellationToken);

            while (await reader.ReadAsync(
                       request.FunctionContext
                           .CancellationToken))
            {
                audits.Add(new
                {
                    DateTimeCreated =
                        reader.GetDateTime(0),

                    ChangedByUserID =
                        reader.IsDBNull(1)
                            ? (int?)null
                            : reader.GetInt32(1),

                    Payload =
                        reader.GetString(2)
                });
            }

            var response =
                request.CreateResponse(
                    HttpStatusCode.OK);

            await response.WriteAsJsonAsync(
                audits);

            return response;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error loading donor audit history for donor {DonorID} in organization {OrganizationID}.",
                donorID,
                orgID);

            var error =
                request.CreateResponse(
                    HttpStatusCode.InternalServerError);

            await error.WriteAsJsonAsync(new
            {
                message =
                    "Unable to load donor history."
            });

            return error;
        }
    }
}
