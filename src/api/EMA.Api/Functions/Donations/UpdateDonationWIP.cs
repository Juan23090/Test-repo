using System.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Donations;

public sealed class UpdateDonationWIP
{
    private readonly ILogger<UpdateDonationWIP>
        _logger;

    private readonly string _connectionString;

    public UpdateDonationWIP(
        ILogger<UpdateDonationWIP> logger)
    {
        _logger =
            logger;

        _connectionString =
            Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString environment variable.");
    }

    [Function("UpdateDonationWIP")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "put",
            Route = "donations/wip/{id:long}")]
        HttpRequest request,
        long id)
    {
        var identity =
            request.GetRequiredApiIdentity();

        try
        {
            using var reader =
                new StreamReader(
                    request.Body);

            var payload =
                await reader.ReadToEndAsync(
                    request.HttpContext
                        .RequestAborted);

            await using var connection =
                new SqlConnection(
                    _connectionString);

            await connection.OpenAsync(
                request.HttpContext
                    .RequestAborted);

            await using var command =
                new SqlCommand(
                    "dbo.sp_DonationWIPUpsert",
                    connection)
                {
                    CommandType =
                        CommandType.StoredProcedure
                };

            command.Parameters.Add(
                "@EMAOrganizationID",
                SqlDbType.Int)
                .Value =
                    identity.EMAOrganizationID;

            command.Parameters.Add(
                "@EMADonationID",
                SqlDbType.BigInt)
                .Value = id;

            command.Parameters.Add(
                "@JsonPayload",
                SqlDbType.NVarChar,
                -1)
                .Value = payload;

            await command.ExecuteNonQueryAsync(
                request.HttpContext
                    .RequestAborted);

            return new NoContentResult();
        }
        catch (SqlException exception)
            when (exception.Number is
                  52121 or 52122)
        {
            _logger.LogWarning(
                exception,
                "Rejected WIP update for donation {EMADonationID} in organization {EMAOrganizationID}.",
                id,
                identity.EMAOrganizationID);

            return new BadRequestObjectResult(
                new
                {
                    message =
                        "The donation WIP data is invalid."
                });
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error updating WIP for donation {EMADonationID} in organization {EMAOrganizationID}.",
                id,
                identity.EMAOrganizationID);

            return new StatusCodeResult(
                StatusCodes
                    .Status500InternalServerError);
        }
    }
}
