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

public sealed class CreateDonor
{
    private readonly ILogger<CreateDonor> _logger;
    private readonly string _connectionString;

    public CreateDonor(
        ILogger<CreateDonor> logger)
    {
        _logger =
            logger;

        _connectionString =
            Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString environment variable.");
    }

    [Function("CreateDonor")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "post",
            Route = "donors")]
        HttpRequest request)
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
                    "dbo.sp_CreateDonor",
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
                Email Ingestion Pilot: normalized to 'Y'/'N' here so a
                client cannot store an arbitrary value in the flag the
                ingestion logic branches on.
            */
            command.Parameters.Add(
                "@IsDonorPilotingEtransfers",
                SqlDbType.Char,
                1)
                .Value =
                    DonorFlagNormalizer.NormalizeForCreate(
                        GetOptionalString(
                            donor,
                            "isDonorPilotingEtransfers"),
                        DonorFlagNormalizer.No);

            /*
                Donor Maintenance: normalized server-side so a client
                cannot store a value outside the CHECK constraint. Anything
                unrecognized falls back to 'Y' ("Receive All"), matching the
                canonical column defaults.
            */
            command.Parameters.Add(
                "@AcknowledgmentForManualTransactions",
                SqlDbType.Char,
                1)
                .Value =
                    DonorFlagNormalizer.NormalizeForCreate(
                        GetOptionalString(
                            donor,
                            "acknowledgmentForManualTransactions"),
                        DonorFlagNormalizer.Yes);

            command.Parameters.Add(
                "@AcknowledgementForAutomatedTransactions",
                SqlDbType.Char,
                1)
                .Value =
                    DonorFlagNormalizer.NormalizeForCreate(
                        GetOptionalString(
                            donor,
                            "acknowledgementForAutomatedTransactions"),
                        DonorFlagNormalizer.Yes);

            command.Parameters.Add(
                "@CreatedByUserID",
                SqlDbType.Int)
                .Value =
                    identity.UserID;

            var result =
                await command.ExecuteScalarAsync(
                    request.HttpContext
                        .RequestAborted);

            if (result is null ||
                result is DBNull)
            {
                throw new InvalidOperationException(
                    "The donor procedure did not return an ID.");
            }

            var newDonorID =
                Convert.ToInt32(
                    result);

            _logger.LogInformation(
                "Created donor {EMADonorID} for organization {EMAOrganizationID} by user {UserID}.",
                newDonorID,
                identity.EMAOrganizationID,
                identity.UserID);

            return new OkObjectResult(
                new
                {
                    EMADonorID =
                        newDonorID
                });
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
                  >= 52001 and <= 52004)
        {
            _logger.LogWarning(
                exception,
                "Rejected donor creation for organization {EMAOrganizationID}.",
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
                "Error creating a donor for organization {EMAOrganizationID}.",
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
