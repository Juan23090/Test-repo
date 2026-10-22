using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Donors;

public sealed class GetDonorById
{
    private readonly ILogger<GetDonorById> _logger;
    private readonly string _connectionString;

    public GetDonorById(
        ILogger<GetDonorById> logger)
    {
        _logger = logger;

        _connectionString =
            Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString.");
    }

    [Function("GetDonorById")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,             "get",
            Route = "donors/single/{id:int}")]
        HttpRequest request,
        int id)
    {
        _logger.LogInformation(
            "GetDonorById triggered for donor {DonorID}.",
            id);

        try
        {
            await using var connection =
                new SqlConnection(
                    _connectionString);

            await connection.OpenAsync();

            const string sql = """
                SELECT
                    EMADonorID,
                    EMAOrganizationID,
                    ChMSDonorID,
                    EMADefaultCampusID,
                    EmailAddress,
                    GivingNumber,
                    InteracDonorName,
                    OfficialDonorName,
                    PhoneNumber,
                    Address,
                    City,
                    ProvinceStateCode,
                    PostalCode,
                    IsDonorPilotingEtransfers,
                    AcknowledgmentForManualTransactions,
                    AcknowledgementForAutomatedTransactions,

                    -- The Donor table currently does not
                    -- contain an IsActive column.
                    CAST(1 AS bit) AS IsActive,

                    DateTimeCreated,
                    CreatedByUserID,
                    DateTimeUpdated,
                    UpdatedbyUserID
                FROM dbo.Donor
                WHERE EMADonorID = @DonorID;
                """;

            await using var command =
                new SqlCommand(
                    sql,
                    connection);

            command.Parameters.AddWithValue(
                "@DonorID",
                id);

            await using var reader =
                await command.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                return new NotFoundObjectResult(new
                {
                    message =
                        $"Donor {id} was not found."
                });
            }

            var donor =
                new Dictionary<string, object?>();

            for (var index = 0;
                 index < reader.FieldCount;
                 index++)
            {
                var columnName =
                    reader.GetName(index);

                donor[columnName] =
                    reader.IsDBNull(index)
                        ? null
                        : reader.GetValue(index);
            }

            return new OkObjectResult(
                donor);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error fetching donor {DonorID}.",
                id);

            return new ObjectResult(new
            {
                message =
                    "Unable to load the donor."
            })
            {
                StatusCode =
                    StatusCodes.Status500InternalServerError
            };
        }
    }
}
