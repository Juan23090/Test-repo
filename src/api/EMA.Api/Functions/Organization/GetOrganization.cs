using System.Data;
using System.Net;
using EMA.Api.DTOs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA_Api.Functions.Organization;

public class GetOrganization
{
    private readonly ILogger<GetOrganization> _logger;
    private readonly string _connectionString;

    public GetOrganization(
        ILogger<GetOrganization> logger)
    {
        _logger =
            logger;

        _connectionString =
            Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString.");
    }

    [Function("GetOrganization")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,             "get",
            Route = "organization/{orgID:int}")]
        HttpRequestData req,
        int orgID)
    {
        _logger.LogInformation(
            "GetOrganization triggered for organization {EMAOrganizationID}.",
            orgID);

        try
        {
            await using var connection =
                new SqlConnection(
                    _connectionString);

            await connection.OpenAsync(
                req.FunctionContext.CancellationToken);

            const string sql = """
                SELECT
                    EMAOrganizationID,
                    AdminName,
                    AdminEmail,
                    AdminEmailVerified,
                    AdminPhone,
                    AdminPhoneVerified,
                    ContactName,
                    ContactEmail,
                    ContactEmailVerified,
                    ContactPhone,
                    OrganizationName,
                    OrganizationPhone,
                    OrganizationEmail,
                    OrganizationEmailVerified,
                    OrganizationWebsite,
                    LogoBlobID,
                    Address,
                    City,
                    ProvinceStateCode,
                    PostalCode,
                    EtransferEmailAddress,
                    EtransferEmailVerified,
                    CRARegisteredCharityNumber,
                    ChMSID,
                    AccountingSoftwareID,
                    DateFormatID,
                    BankBranchNumber,
                    BankAccountNumber,
                    EMAMethodOfGivingIDForCheques,
                    EMAMethodOfGivingIDForCash,
                    CountingSteps,
                    AnnualBudget,
                    OrganizationStatusID,
                    EMAEventIDForPADs,
                    EMASourceIDForPADs,
                    EMAMethodOfGivingIDForPADs,
                    EMAFundIDForPADs,
                    EMACampusIDForPADs,
                    ETLDonorDataBlobID,
                    ETLDonationDataBlobID,
                    DateTimeUpdated,
                    UpdatedByUserID,
                    DateTimeCreated,
                    CreatedByUserID
                FROM dbo.Organization
                WHERE EMAOrganizationID = @EMAOrganizationID;
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

            await using var reader =
                await command.ExecuteReaderAsync(
                    req.FunctionContext.CancellationToken);

            if (!await reader.ReadAsync(
                    req.FunctionContext.CancellationToken))
            {
                var notFound =
                    req.CreateResponse(
                        HttpStatusCode.NotFound);

                await notFound.WriteAsJsonAsync(new
                {
                    message =
                        $"Organization {orgID} not found."
                });

                return notFound;
            }

            var organization =
                new OrganizationDTO
                {
                    EMAOrganizationID =
                        GetInt(
                            reader,
                            "EMAOrganizationID"),

                    AdminName =
                        GetString(
                            reader,
                            "AdminName"),

                    AdminEmail =
                        GetString(
                            reader,
                            "AdminEmail"),

                    AdminEmailVerified =
                        GetNullableChar(
                            reader,
                            "AdminEmailVerified"),

                    AdminPhone =
                        GetString(
                            reader,
                            "AdminPhone"),

                    AdminPhoneVerified =
                        GetNullableChar(
                            reader,
                            "AdminPhoneVerified"),

                    ContactName =
                        GetString(
                            reader,
                            "ContactName"),

                    ContactEmail =
                        GetString(
                            reader,
                            "ContactEmail"),

                    ContactEmailVerified =
                        GetNullableChar(
                            reader,
                            "ContactEmailVerified"),

                    ContactPhone =
                        GetString(
                            reader,
                            "ContactPhone"),

                    OrganizationName =
                        GetString(
                            reader,
                            "OrganizationName"),

                    OrganizationPhone =
                        GetString(
                            reader,
                            "OrganizationPhone"),

                    OrganizationEmail =
                        GetString(
                            reader,
                            "OrganizationEmail"),

                    OrganizationEmailVerified =
                        GetNullableChar(
                            reader,
                            "OrganizationEmailVerified"),

                    OrganizationWebsite =
                        GetString(
                            reader,
                            "OrganizationWebsite"),

                    LogoBlobID =
                        GetString(
                            reader,
                            "LogoBlobID"),

                    Address =
                        GetString(
                            reader,
                            "Address"),

                    City =
                        GetString(
                            reader,
                            "City"),

                    ProvinceStateCode =
                        GetString(
                            reader,
                            "ProvinceStateCode"),

                    PostalCode =
                        GetString(
                            reader,
                            "PostalCode"),

                    EtransferEmailAddress =
                        GetString(
                            reader,
                            "EtransferEmailAddress"),

                    EtransferEmailVerified =
                        GetNullableChar(
                            reader,
                            "EtransferEmailVerified"),

                    CRARegisteredCharityNumber =
                        GetString(
                            reader,
                            "CRARegisteredCharityNumber"),

                    ChMSID =
                        GetNullableInt(
                            reader,
                            "ChMSID"),

                    AccountingSoftwareID =
                        GetNullableInt(
                            reader,
                            "AccountingSoftwareID"),

                    DateFormatID =
                        GetString(
                            reader,
                            "DateFormatID"),

                    BankBranchNumber =
                        GetString(
                            reader,
                            "BankBranchNumber"),

                    BankAccountNumber =
                        GetString(
                            reader,
                            "BankAccountNumber"),

                    EMAMethodOfGivingIDForCheques =
                        GetNullableInt(
                            reader,
                            "EMAMethodOfGivingIDForCheques"),

                    EMAMethodOfGivingIDForCash =
                        GetNullableInt(
                            reader,
                            "EMAMethodOfGivingIDForCash"),

                    CountingSteps =
                        GetString(
                            reader,
                            "CountingSteps"),

                    AnnualBudget =
                        GetString(
                            reader,
                            "AnnualBudget"),

                    OrganizationStatusID =
                        GetNullableInt(
                            reader,
                            "OrganizationStatusID"),

                    EMAEventIDForPADs =
                        GetNullableInt(
                            reader,
                            "EMAEventIDForPADs"),

                    EMASourceIDForPADs =
                        GetNullableInt(
                            reader,
                            "EMASourceIDForPADs"),

                    EMAMethodOfGivingIDForPADs =
                        GetNullableInt(
                            reader,
                            "EMAMethodOfGivingIDForPADs"),

                    EMAFundIDForPADs =
                        GetNullableInt(
                            reader,
                            "EMAFundIDForPADs"),

                    EMACampusIDForPADs =
                        GetNullableInt(
                            reader,
                            "EMACampusIDForPADs"),

                    ETLDonorDataBlobID =
                        GetString(
                            reader,
                            "ETLDonorDataBlobID"),

                    ETLDonationDataBlobID =
                        GetString(
                            reader,
                            "ETLDonationDataBlobID"),

                    DateTimeUpdated =
                        GetNullableDateTime(
                            reader,
                            "DateTimeUpdated"),

                    UpdatedByUserID =
                        GetNullableInt(
                            reader,
                            "UpdatedByUserID"),

                    DateTimeCreated =
                        GetNullableDateTime(
                            reader,
                            "DateTimeCreated"),

                    CreatedByUserID =
                        GetNullableInt(
                            reader,
                            "CreatedByUserID")
                };

            var response =
                req.CreateResponse(
                    HttpStatusCode.OK);

            await response.WriteAsJsonAsync(
                organization);

            return response;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error fetching organization {EMAOrganizationID}.",
                orgID);

            var error =
                req.CreateResponse(
                    HttpStatusCode.InternalServerError);

            await error.WriteAsJsonAsync(new
            {
                message =
                    "Error fetching organization."
            });

            return error;
        }
    }

    private static string? GetString(
        SqlDataReader reader,
        string column)
    {
        var ordinal =
            reader.GetOrdinal(column);

        return reader.IsDBNull(ordinal)
            ? null
            : reader
                .GetValue(ordinal)
                ?.ToString();
    }

    private static int GetInt(
        SqlDataReader reader,
        string column)
    {
        var ordinal =
            reader.GetOrdinal(column);

        return reader.IsDBNull(ordinal)
            ? 0
            : Convert.ToInt32(
                reader.GetValue(ordinal));
    }

    private static int? GetNullableInt(
        SqlDataReader reader,
        string column)
    {
        var ordinal =
            reader.GetOrdinal(column);

        return reader.IsDBNull(ordinal)
            ? null
            : Convert.ToInt32(
                reader.GetValue(ordinal));
    }

    private static DateTime? GetNullableDateTime(
        SqlDataReader reader,
        string column)
    {
        var ordinal =
            reader.GetOrdinal(column);

        return reader.IsDBNull(ordinal)
            ? null
            : Convert.ToDateTime(
                reader.GetValue(ordinal));
    }

    private static char? GetNullableChar(
        SqlDataReader reader,
        string column)
    {
        var ordinal =
            reader.GetOrdinal(column);

        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value =
            reader
                .GetValue(ordinal)
                ?.ToString();

        return string.IsNullOrWhiteSpace(value)
            ? null
            : value[0];
    }
}
