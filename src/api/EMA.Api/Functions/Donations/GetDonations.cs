using System.Data;
using System.Globalization;
using System.Net;
using EMA.Api.DTOs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Donations;

public class GetDonations
{
    private readonly ILogger<GetDonations> _logger;
    private readonly string _connectionString;

    public GetDonations(
        ILogger<GetDonations> logger)
    {
        _logger =
            logger;

        _connectionString =
            Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing 'SqlConnectionString' env var.");
    }

    [Function("GetDonations")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "donations/{orgID:int}")]
        HttpRequestData req,
        int orgID)
    {
        try
        {
            long? donorID =
                long.TryParse(
                    req.Query["donorID"],
                    out var parsedDonorID) &&
                parsedDonorID > 0
                    ? parsedDonorID
                    : null;

            DateTime? receivedDate =
                DateTime.TryParseExact(
                    req.Query["receivedDate"],
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsedReceivedDate)
                    ? parsedReceivedDate.Date
                    : null;

            await using var connection =
                new SqlConnection(
                    _connectionString);

            await connection.OpenAsync(
                req.FunctionContext.CancellationToken);

            const string sql = """
                SELECT
                    d.EMADonationID,
                    d.EMAOrganizationID,
                    d.EMADonorID,
                    COALESCE(
                        NULLIF(d.InteracDonorName, ''),
                        NULLIF(dn.OfficialDonorName, ''),
                        dn.InteracDonorName) AS InteracDonorName,
                    d.Comments,
                    d.DateTimeReceived,
                    d.DonorToOrgChangeRequest,
                    d.EMADonationSourceID,
                    d.EMAMethodOfGivingID,
                    d.EMAEventID,
                    d.GlobalMessageID,
                    d.ReferenceNumber,
                    d.DonationAmount,
                    d.TributeText,
                    d.EMATributeTypeID,
                    d.IsTemplate,
                    d.DateTimeCreated,
                    d.CreatedbyUserID,
                    d.DateTimeUpdated,
                    d.UpdatedbyUserID
                FROM dbo.Donation d
                LEFT JOIN dbo.Donor dn
                    ON d.EMADonorID = dn.EMADonorID
                   AND d.EMAOrganizationID = dn.EMAOrganizationID
                WHERE d.EMAOrganizationID = @EMAOrganizationID
                  AND (@EMADonorID IS NULL OR d.EMADonorID = @EMADonorID)
                  AND (
                      @ReceivedDate IS NULL OR
                      (
                          d.DateTimeReceived >= @ReceivedDate AND
                          d.DateTimeReceived < DATEADD(DAY, 1, @ReceivedDate)
                      )
                  )
                ORDER BY
                    d.DateTimeReceived DESC,
                    d.EMADonationID DESC;
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
                    orgID;

            command.Parameters
                .Add(
                    "@EMADonorID",
                    SqlDbType.BigInt)
                .Value =
                    donorID.HasValue
                        ? donorID.Value
                        : DBNull.Value;

            command.Parameters
                .Add(
                    "@ReceivedDate",
                    SqlDbType.Date)
                .Value =
                    receivedDate.HasValue
                        ? receivedDate.Value
                        : DBNull.Value;

            var donations =
                new List<DonationDTO>();

            await using var reader =
                await command.ExecuteReaderAsync(
                    req.FunctionContext.CancellationToken);

            while (await reader.ReadAsync(
                       req.FunctionContext.CancellationToken))
            {
                donations.Add(
                    DonationFunctionHelpers.ReadDonation(
                        reader));
            }

            var response =
                req.CreateResponse(
                    HttpStatusCode.OK);

            await response.WriteAsJsonAsync(
                donations);

            return response;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error loading donations for organization {OrgID}.",
                orgID);

            var error =
                req.CreateResponse(
                    HttpStatusCode.InternalServerError);

            await error.WriteAsJsonAsync(new
            {
                message =
                    "Unable to load donations."
            });

            return error;
        }
    }
}