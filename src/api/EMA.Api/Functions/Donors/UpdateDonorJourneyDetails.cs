using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace EMA.Api.Functions.Donors;

public sealed class UpdateDonorJourneyDetails
{
    private readonly ILogger<UpdateDonorJourneyDetails> _logger;
    private readonly string _connectionString;

    public UpdateDonorJourneyDetails(ILogger<UpdateDonorJourneyDetails> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")
            ?? throw new InvalidOperationException("Missing SqlConnectionString.");
    }

    [Function("UpdateDonorJourneyDetails")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "donors/journey-update/{orgID:int}/{id:int}")] HttpRequest request,
        int orgID,
        int id)
    {
        _logger.LogInformation("UpdateDonorJourneyDetails triggered for donor ID {ID} under org {OrgID}.", id, orgID);

        try
        {
            var body = await new StreamReader(request.Body).ReadToEndAsync();
            var json = JsonSerializer.Deserialize<JsonElement>(body);

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            string sql = """
                UPDATE dbo.Donor
                SET EmailAddress = @EmailAddress,
                    InteracDonorName = @InteracDonorName,
                    OfficialDonorName = @OfficialDonorName,
                    Address = @Address,
                    City = @City,
                    ProvinceStateCode = @ProvinceStateCode,
                    PostalCode = @PostalCode,
                    Country = @Country,
                    PhoneNumber = @PhoneNumber,
                    DonorStatusID = @DonorStatusID,
                    -- Schema detail: 'UpdatedbyUserID' uses a lowercase 'by' in this table.
                    UpdatedbyUserID = @UpdatedByUserID,
                    DateTimeUpdated = GETDATE()
                WHERE EMADonorID = @EMADonorID AND EMAOrganizationID = @OrgID;
                """;

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@EMADonorID", id);
            command.Parameters.AddWithValue("@OrgID", orgID);

            command.Parameters.AddWithValue("@EmailAddress", GetValueOrDbNull(json, "emailAddress"));
            command.Parameters.AddWithValue("@InteracDonorName", GetValueOrDbNull(json, "interacDonorName"));
            command.Parameters.AddWithValue("@OfficialDonorName", GetValueOrDbNull(json, "officialDonorName"));
            command.Parameters.AddWithValue("@Address", GetValueOrDbNull(json, "address"));
            command.Parameters.AddWithValue("@City", GetValueOrDbNull(json, "city"));
            command.Parameters.AddWithValue("@ProvinceStateCode", GetValueOrDbNull(json, "provinceStateCode"));
            command.Parameters.AddWithValue("@PostalCode", GetValueOrDbNull(json, "postalCode"));
            command.Parameters.AddWithValue("@Country", GetValueOrDbNull(json, "country"));
            command.Parameters.AddWithValue("@PhoneNumber", GetValueOrDbNull(json, "phoneNumber"));
            command.Parameters.AddWithValue("@DonorStatusID", GetValueOrDbNull(json, "donorStatusID"));

            command.Parameters.AddWithValue(
                "@UpdatedByUserID",
                request.GetRequiredApiIdentity().UserID);

            int rowsAffected = await command.ExecuteNonQueryAsync();

            if (rowsAffected == 0)
            {
                return new NotFoundObjectResult(new { message = $"Donor with ID {id} was not found." });
            }

            return new OkResult();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error updating donor journey details for donor ID {ID}.", id);

            return new ObjectResult(new { message = "Unable to update donor details." })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }

    private static object GetValueOrDbNull(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind != JsonValueKind.Null)
        {
            return prop.ValueKind switch
            {
                JsonValueKind.String => prop.GetString() ?? (object)DBNull.Value,
                JsonValueKind.Number => prop.TryGetInt32(out int i) ? (object)i : (object)prop.GetDecimal(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => DBNull.Value
            };
        }
        return DBNull.Value;
    }
}
