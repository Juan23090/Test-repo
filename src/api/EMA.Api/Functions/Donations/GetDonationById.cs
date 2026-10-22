using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Donations;

public class GetDonationByID
{
    private readonly ILogger<GetDonationByID> _logger;
    private readonly string _connectionString;

    public GetDonationByID(
        ILogger<GetDonationByID> logger)
    {
        _logger =
            logger;

        _connectionString =
            Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing 'SqlConnectionString' env var.");
    }

    [Function("GetDonationByID")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "donations/{orgID:int}/{donationID:long}")]
        HttpRequestData req,
        int orgID,
        long donationID)
    {
        try
        {
            await using var connection =
                new SqlConnection(
                    _connectionString);

            await connection.OpenAsync(
                req.FunctionContext.CancellationToken);

            var donation =
                await DonationFunctionHelpers.GetDonationAsync(
                    connection,
                    transaction: null,
                    orgID,
                    donationID,
                    req.FunctionContext.CancellationToken);

            if (donation is null)
            {
                var notFound =
                    req.CreateResponse(
                        HttpStatusCode.NotFound);

                await notFound.WriteAsJsonAsync(new
                {
                    message =
                        "Donation not found."
                });

                return notFound;
            }

            if (string.Equals(
                    donation.DonorToOrgChangeRequest,
                    "Y",
                    StringComparison.OrdinalIgnoreCase))
            {
                await DonationFunctionHelpers.LogStepAsync(
                    connection,
                    transaction: null,
                    donationID,
                    DonationFunctionHelpers.OfficeNotificationLoadedStepID,
                    refreshLoggedTime: false,
                    req.FunctionContext.CancellationToken);
            }

            var response =
                req.CreateResponse(
                    HttpStatusCode.OK);

            await response.WriteAsJsonAsync(
                donation);

            return response;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error loading donation {DonationID} for organization {OrgID}.",
                donationID,
                orgID);

            var error =
                req.CreateResponse(
                    HttpStatusCode.InternalServerError);

            await error.WriteAsJsonAsync(new
            {
                message =
                    "Unable to load the donation."
            });

            return error;
        }
    }
}
