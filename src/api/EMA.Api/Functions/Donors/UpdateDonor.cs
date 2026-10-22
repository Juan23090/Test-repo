using System.Data;
using System.Text.Json;
using EMA.Api.Services.Donors;
using EMA.Api.Services.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA_Api.Functions.Donors;

public sealed class UpdateDonor
{
    private readonly ILogger<UpdateDonor> _logger;
    private readonly string _connectionString;

    public UpdateDonor(
        ILogger<UpdateDonor> logger)
    {
        _logger =
            logger;

        _connectionString =
            Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString environment variable.");
    }

    [Function("UpdateDonor")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "put",
            Route = "donors/{id:int}")]
        HttpRequest request,
        int id)
    {
        var identity =
            request.GetRequiredApiIdentity();

        try
        {
            using var document =
                await JsonDocument.ParseAsync(
                    request.Body,
                    cancellationToken:
                        request.HttpContext
                            .RequestAborted);

            var donor =
                document.RootElement;

            await using var connection =
                new SqlConnection(
                    _connectionString);

            await connection.OpenAsync(
                request.HttpContext
                    .RequestAborted);

            await using var command =
                new SqlCommand(
                    "dbo.sp_UpdateDonor",
                    connection)
                {
                    CommandType =
                        CommandType.StoredProcedure
                };

            command.Parameters.Add(
                "@EMADonorID",
                SqlDbType.Int)
                .Value = id;

            command.Parameters.Add(
                "@EMAOrganizationID",
                SqlDbType.Int)
                .Value =
                    identity.EMAOrganizationID;

            AddNullableString(
                command,
                "@EmailAddress",
                SqlDbType.VarChar,
                GetOptionalString(
                    donor,
                    "emailAddress"));

            AddNullableString(
                command,
                "@InteracDonorName",
                SqlDbType.VarChar,
                GetOptionalString(
                    donor,
                    "interacDonorName"));

            AddNullableString(
                command,
                "@Name",
                SqlDbType.VarChar,
                GetOptionalString(
                    donor,
                    "name"));

            AddNullableString(
                command,
                "@Address",
                SqlDbType.VarChar,
                GetOptionalString(
                    donor,
                    "address"));

            AddNullableString(
                command,
                "@City",
                SqlDbType.VarChar,
                GetOptionalString(
                    donor,
                    "city"));

            AddNullableString(
                command,
                "@ProvinceStateCode",
                SqlDbType.Char,
                EMA.Api.Services.Authentication.ProvinceStateCodeNormalizer.Normalize(
                    GetOptionalString(
                        donor,
                        "provinceStateCode")),
                size: 2);

            AddNullableString(
                command,
                "@PostalCode",
                SqlDbType.VarChar,
                GetOptionalString(
                    donor,
                    "postalCode"));

            AddNullableString(
                command,
                "@Country",
                SqlDbType.VarChar,
                GetOptionalString(
                    donor,
                    "country"));

            AddNullableString(
                command,
                "@PhoneNumber",
                SqlDbType.VarChar,
                GetOptionalString(
                    donor,
                    "phoneNumber"));

            command.Parameters.Add(
                "@EMADefaultCampusID",
                SqlDbType.Int)
                .Value =
                    GetOptionalPositiveInt(
                        donor,
                        "emaCampusID")
                    ?? (object)DBNull.Value;

            command.Parameters.Add(
                "@DonorStatusID",
                SqlDbType.Int)
                .Value =
                    GetOptionalPositiveInt(
                        donor,
                        "donorStatusID")
                    ?? (object)DBNull.Value;

            /*
                All three flags use preserve-on-unknown semantics: an update
                caller that omits the field, or sends something other than
                'Y'/'N', leaves the donor's stored value alone. A partial
                caller must not be able to reset a flag it does not know
                about - for the pilot flag that would silently reroute the
                donor's email.
            */
            command.Parameters.Add(
                "@IsDonorPilotingEtransfers",
                SqlDbType.Char,
                1)
                .Value =
                    DonorFlagNormalizer.NormalizeForUpdate(
                        GetOptionalString(
                            donor,
                            "isDonorPilotingEtransfers"));

            command.Parameters.Add(
                "@AcknowledgmentForManualTransactions",
                SqlDbType.Char,
                1)
                .Value =
                    DonorFlagNormalizer.NormalizeForUpdate(
                        GetOptionalString(
                            donor,
                            "acknowledgmentForManualTransactions"));

            command.Parameters.Add(
                "@AcknowledgementForAutomatedTransactions",
                SqlDbType.Char,
                1)
                .Value =
                    DonorFlagNormalizer.NormalizeForUpdate(
                        GetOptionalString(
                            donor,
                            "acknowledgementForAutomatedTransactions"));

            command.Parameters.Add(
                "@UpdatedByUserID",
                SqlDbType.Int)
                .Value =
                    identity.UserID;

            await command.ExecuteNonQueryAsync(
                request.HttpContext
                    .RequestAborted);

            _logger.LogInformation(
                "Updated donor {EMADonorID} for organization {EMAOrganizationID} by user {UserID}.",
                id,
                identity.EMAOrganizationID,
                identity.UserID);

            return new NoContentResult();
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult(
                new
                {
                    message =
                        "The donor payload is invalid."
                });
        }
        catch (SqlException exception)
            when (exception.Number is
                  >= 52011 and <= 52014)
        {
            _logger.LogWarning(
                exception,
                "Rejected update for donor {EMADonorID} in organization {EMAOrganizationID}.",
                id,
                identity.EMAOrganizationID);

            return new BadRequestObjectResult(
                new
                {
                    message =
                        "The donor data is invalid."
                });
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error updating donor {EMADonorID} in organization {EMAOrganizationID}.",
                id,
                identity.EMAOrganizationID);

            return new StatusCodeResult(
                StatusCodes
                    .Status500InternalServerError);
        }
    }

    private static string?
        GetOptionalString(
            JsonElement element,
            string propertyName)
    {
        if (!element.TryGetProperty(
                propertyName,
                out var property) ||
            property.ValueKind is
                JsonValueKind.Null or
                JsonValueKind.Undefined)
        {
            return null;
        }

        if (property.ValueKind !=
            JsonValueKind.String)
        {
            throw new JsonException(
                $"{propertyName} must be a string.");
        }

        return property.GetString()?
            .Trim();
    }

    private static int?
        GetOptionalPositiveInt(
            JsonElement element,
            string propertyName)
    {
        if (!element.TryGetProperty(
                propertyName,
                out var property) ||
            property.ValueKind is
                JsonValueKind.Null or
                JsonValueKind.Undefined)
        {
            return null;
        }

        if (!property.TryGetInt32(
                out var value) ||
            value <= 0)
        {
            throw new JsonException(
                $"{propertyName} must be a positive integer.");
        }

        return value;
    }

    private static void AddNullableString(
        SqlCommand command,
        string parameterName,
        SqlDbType type,
        string? value,
        int size = -1)
    {
        command.Parameters.Add(
            parameterName,
            type,
            size)
            .Value =
                string.IsNullOrWhiteSpace(
                    value)
                    ? DBNull.Value
                    : value;
    }
}
