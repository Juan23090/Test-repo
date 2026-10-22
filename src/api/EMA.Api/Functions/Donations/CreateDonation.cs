using System.Data;
using System.Net;
using EMA.Api.DTOs;
using EMA.Api.Services.Email;
using EMA.Api.Services.Security;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Donations;

public class CreateDonation
{
    private readonly ILogger<CreateDonation> _logger;
    private readonly IEmailNotificationService
        _emailNotificationService;
    private readonly string _connectionString;

    public CreateDonation(
        ILogger<CreateDonation> logger,
        IEmailNotificationService emailNotificationService)
    {
        _logger =
            logger;

        _emailNotificationService =
            emailNotificationService;

        _connectionString =
            Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing 'SqlConnectionString' env var.");
    }

    [Function("CreateDonation")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "post",
            Route = "donations/{orgID:int}")]
        HttpRequestData req,
        int orgID)
    {
        try
        {
            // Identity comes from the validated access token. The route
            // organization is only ever checked against it, never trusted.
            var identity =
                req.GetRequiredApiIdentity();

            if (!DonationWriteAccessPolicy
                    .IsRouteOrganizationAllowed(
                        identity,
                        orgID))
            {
                return await CreateErrorResponseAsync(
                    req,
                    HttpStatusCode.Forbidden,
                    "The requested organization does not match the authenticated session.");
            }

            var donation =
                await req.ReadFromJsonAsync<DonationDTO>();

            if (donation is null)
            {
                return await CreateErrorResponseAsync(
                    req,
                    HttpStatusCode.BadRequest,
                    "Body is required.");
            }

            donation.EMAOrganizationID =
                DonationWriteAccessPolicy
                    .ResolveOrganizationID(
                        identity);

            DonationFunctionHelpers.ValidateDonation(
                donation);

            await using var connection =
                new SqlConnection(
                    _connectionString);

            await connection.OpenAsync(
                req.FunctionContext.CancellationToken);

            await using var transaction =
                (SqlTransaction)await connection.BeginTransactionAsync(
                    req.FunctionContext.CancellationToken);

            var donorName =
                await DonationFunctionHelpers.GetDonorNameAsync(
                    connection,
                    transaction,
                    identity.EMAOrganizationID,
                    donation.EMADonorID,
                    req.FunctionContext.CancellationToken);

            if (donorName is null)
            {
                await transaction.RollbackAsync(
                    req.FunctionContext.CancellationToken);

                return await CreateErrorResponseAsync(
                    req,
                    HttpStatusCode.BadRequest,
                    "The selected donor does not belong to this organization.");
            }

            const string sql = """
                INSERT INTO dbo.Donation
                (
                    EMAOrganizationID,
                    EMADonorID,
                    InteracDonorName,
                    DateTimeReceived,
                    EMADonationSourceID,
                    EMAMethodOfGivingID,
                    EMAEventID,
                    ReferenceNumber,
                    Comments,
                    DonationAmount,
                    TributeText,
                    EMATributeTypeID,
                    IsTemplate,
                    DateTimeCreated,
                    CreatedbyUserID,
                    DateTimeUpdated,
                    UpdatedbyUserID
                )
                OUTPUT INSERTED.EMADonationID
                VALUES
                (
                    @EMAOrganizationID,
                    @EMADonorID,
                    @InteracDonorName,
                    @DateTimeReceived,
                    @EMADonationSourceID,
                    @EMAMethodOfGivingID,
                    @EMAEventID,
                    @ReferenceNumber,
                    @Comments,
                    @DonationAmount,
                    @TributeText,
                    @EMATributeTypeID,
                    @IsTemplate,
                    @DateTimeCreated,
                    @UserID,
                    @DateTimeUpdated,
                    @UserID
                );
                """;

            await using var command =
                new SqlCommand(
                    sql,
                    connection,
                    transaction);

            // Never trust CreatedbyUserID / UpdatedbyUserID from the client.
            var userID =
                DonationWriteAccessPolicy
                    .ResolveActingUserID(
                        identity);

            DonationFunctionHelpers.AddDonationParameters(
                command,
                donation,
                donorName,
                userID);

            command.Parameters
                .Add(
                    "@DateTimeCreated",
                    SqlDbType.DateTime2)
                .Value =
                    DateTime.UtcNow;

            command.Parameters
                .Add(
                    "@DateTimeUpdated",
                    SqlDbType.DateTime2)
                .Value =
                    DateTime.UtcNow;

            var result =
                await command.ExecuteScalarAsync(
                    req.FunctionContext.CancellationToken);

            if (result is null or DBNull)
            {
                throw new InvalidOperationException(
                    "The donation was not created.");
            }

            var donationID =
                Convert.ToInt64(
                    result);

            await DonationFunctionHelpers.ReplaceDistributionsAsync(
                connection,
                transaction,
                donationID,
                donation.Distributions,
                req.FunctionContext.CancellationToken);

            await DonationFunctionHelpers.LogStepAsync(
                connection,
                transaction,
                donationID,
                DonationFunctionHelpers.ManuallyKeyedDonationCreatedStepID,
                refreshLoggedTime: false,
                req.FunctionContext.CancellationToken);

            await transaction.CommitAsync(
                req.FunctionContext.CancellationToken);

            try
            {
                var acknowledgementResult =
                    await _emailNotificationService
                        .SendDonationAcknowledgementAsync(
                            donationID,
                            DonationTransactionType.Manual,
                            req.FunctionContext.CancellationToken);

                _logger.LogInformation(
                    "Manual donation acknowledgement result for DonationID={DonationID}: {Status}. {Message}",
                    donationID,
                    acknowledgementResult.Status,
                    acknowledgementResult.Message);
            }
            catch (Exception acknowledgementException)
            {
                // The donation is already committed. A notification failure
                // must not cause the client to retry and create a duplicate.
                _logger.LogError(
                    acknowledgementException,
                    "Donation {DonationID} was created, but its manual acknowledgement could not be processed.",
                    donationID);
            }

            _logger.LogInformation(
                "Created donation {EMADonationID} for organization {EMAOrganizationID} by user {UserID}.",
                donationID,
                identity.EMAOrganizationID,
                identity.UserID);

            var created =
                req.CreateResponse(
                    HttpStatusCode.Created);

            await created.WriteAsJsonAsync(new
            {
                donationID
            });

            return created;
        }
        catch (UnauthorizedAccessException)
        {
            return await CreateErrorResponseAsync(
                req,
                HttpStatusCode.Unauthorized,
                "An authenticated session is required.");
        }
        catch (ArgumentException exception)
        {
            return await CreateErrorResponseAsync(
                req,
                HttpStatusCode.BadRequest,
                exception.Message);
        }
        catch (SqlException exception)
            when (DonationFunctionHelpers
                .IsDuplicateReferenceNumberException(
                    exception))
        {
            return await CreateErrorResponseAsync(
                req,
                HttpStatusCode.Conflict,
                "A donation with this reference number already exists.");
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error creating donation for organization {OrgID}.",
                orgID);

            return await CreateErrorResponseAsync(
                req,
                HttpStatusCode.InternalServerError,
                "Error creating donation.");
        }
    }

    private static async Task<HttpResponseData> CreateErrorResponseAsync(
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
