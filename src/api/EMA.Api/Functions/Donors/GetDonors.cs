using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Donors;

public sealed class GetDonors
{
    
    private readonly ILogger<GetDonors> _logger;
    private readonly string _connectionString;

    public GetDonors(
        ILogger<GetDonors> logger)
    {
        _logger = logger;

        _connectionString =
            Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString.");
    }

    [Function("GetDonors")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,             "get",
            Route = "donors/{orgID:int}")]
        HttpRequest request,
        int orgID)
    {
        _logger.LogInformation(
            "GetDonors triggered for org {OrgID}.",
            orgID);

        try
        {
            var donors =
                new List<Dictionary<string, object?>>();

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
                    Country,
                    DonorStatusID,
                    IsDonorPilotingEtransfers,
                    AcknowledgmentForManualTransactions,
                    AcknowledgementForAutomatedTransactions,

                    -- dbo.Donor currently does not contain
                    -- an IsActive column. Return a compatible
                    -- value for the Blazor donor model.
                    CAST(1 AS bit) AS IsActive,

                    DateTimeCreated,
                    CreatedByUserID,
                    DateTimeUpdated,
                    UpdatedbyUserID
                FROM dbo.Donor
                WHERE EMAOrganizationID = @OrgID
                ORDER BY OfficialDonorName;
                """;

            await using var command =
                new SqlCommand(
                    sql,
                    connection);

            command.Parameters.AddWithValue(
                "@OrgID",
                orgID);

            await using var reader =
                await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
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

                donors.Add(donor);
            }

            return new OkObjectResult(
                donors);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error fetching donors for organization {OrgID}.",
                orgID);

            return new ObjectResult(new
            {
                message =
                    "Unable to load donors."
            })
            {
                StatusCode =
                    StatusCodes.Status500InternalServerError
            };
        }
    }
}
