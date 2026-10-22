using System.Data;
using System.Net;
using EMA.Api.DTOs;
using EMA.Api.Services.Security;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Donations;

public class UpdateDonation
{
    private readonly ILogger<UpdateDonation> _logger;
    private readonly string _connectionString;

    public UpdateDonation(
        ILogger<UpdateDonation> logger)
    {
        _logger =
            logger;

        _connectionString =
            Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing 'SqlConnectionString' env var.");
    }

    [Function("UpdateDonation")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "put",
            Route = "donations/{orgID:int}/{donationID:long}")]
        HttpRequestData req,
        int orgID,
        long donationID)
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

            donation.EMADonationID =
                donationID;

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

            var existingDonation =
                await DonationFunctionHelpers.GetDonationAsync(
                    connection,
                    transaction,
                    identity.EMAOrganizationID,
                    donationID,
                    req.FunctionContext.CancellationToken);

            if (existingDonation is null)
            {
                await transaction.RollbackAsync(
                    req.FunctionContext.CancellationToken);

                return await CreateErrorResponseAsync(
                    req,
                    HttpStatusCode.NotFound,
                    "Donation not found.");
            }

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

            // Resolve the acting user from the authenticated identity before
            // writing the snapshot so the audit row identifies this edit.
            var userID =
                DonationWriteAccessPolicy
                    .ResolveActingUserID(
                        identity);

            await DonationFunctionHelpers.WriteAuditAsync(
                connection,
                transaction,
                existingDonation,
                userID,
                req.FunctionContext.CancellationToken);

            const string sql = """
                UPDATE dbo.Donation
                SET
                    EMADonorID          = @EMADonorID,
                    InteracDonorName    = @InteracDonorName,
                    DateTimeReceived    = @DateTimeReceived,
                    EMADonationSourceID = @EMADonationSourceID,
                    EMAMethodOfGivingID = @EMAMethodOfGivingID,
                    EMAEventID          = @EMAEventID,
                    ReferenceNumber     = @ReferenceNumber,
                    Comments            = @Comments,
                    DonationAmount      = @DonationAmount,
                    TributeText         = @TributeText,
                    EMATributeTypeID     = @EMATributeTypeID,
                    IsTemplate          = @IsTemplate,
                    DonorToOrgChangeRequest =
                        CASE
                            WHEN @ClearOfficeNotification = 1
                                THEN 'N'
                            ELSE DonorToOrgChangeRequest
                        END,
                    DateTimeUpdated     = @DateTimeUpdated,
                    UpdatedbyUserID      = @UserID
                WHERE EMAOrganizationID = @EMAOrganizationID
                  AND EMADonationID = @EMADonationID;
                """;

            await using var command =
                new SqlCommand(
                    sql,
                    connection,
                    transaction);

            // Never trust UpdatedbyUserID / CreatedbyUserID from the client.
            DonationFunctionHelpers.AddDonationParameters(
                command,
                donation,
                donorName,
                userID);

            command.Parameters
                .Add(
                    "@EMADonationID",
                    SqlDbType.BigInt)
                .Value =
                    donationID;

            var hasOfficeNotification =
                string.Equals(
                    existingDonation.DonorToOrgChangeRequest,
                    "Y",
                    StringComparison.OrdinalIgnoreCase);

            command.Parameters
                .Add(
                    "@ClearOfficeNotification",
                    SqlDbType.Bit)
                .Value =
                    hasOfficeNotification;

            command.Parameters
                .Add(
                    "@DateTimeUpdated",
                    SqlDbType.DateTime2)
                .Value =
                    DateTime.UtcNow;

            var rows =
                await command.ExecuteNonQueryAsync(
                    req.FunctionContext.CancellationToken);

            if (rows == 0)
            {
                await transaction.RollbackAsync(
                    req.FunctionContext.CancellationToken);

                return await CreateErrorResponseAsync(
                    req,
                    HttpStatusCode.NotFound,
                    "Donation not found.");
            }

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
                DonationFunctionHelpers.ManuallyEditedDonationStepID,
                refreshLoggedTime: true,
                req.FunctionContext.CancellationToken);

            if (hasOfficeNotification)
            {
                await DonationFunctionHelpers.LogStepAsync(
                    connection,
                    transaction,
                    donationID,
                    DonationFunctionHelpers.OfficeNotificationSavedStepID,
                    refreshLoggedTime: false,
                    req.FunctionContext.CancellationToken);
            }

            await transaction.CommitAsync(
                req.FunctionContext.CancellationToken);

            _logger.LogInformation(
                "Updated donation {EMADonationID} for organization {EMAOrganizationID} by user {UserID}.",
                donationID,
                identity.EMAOrganizationID,
                identity.UserID);

            return req.CreateResponse(
                HttpStatusCode.NoContent);
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
                "Error updating donation {DonationID} for organization {OrgID}.",
                donationID,
                orgID);

            return await CreateErrorResponseAsync(
                req,
                HttpStatusCode.InternalServerError,
                "Error updating donation.");
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
