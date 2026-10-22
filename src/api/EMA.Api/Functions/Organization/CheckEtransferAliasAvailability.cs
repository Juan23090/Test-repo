using System.Data;
using System.Net;
using System.Web;
using EMA_Api.Services.EmailAliases;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA_Api.Functions.Organization;

public sealed class CheckEtransferAliasAvailability
{
    private readonly ILogger<CheckEtransferAliasAvailability> _logger;
    private readonly IEtransferEmailAliasService _aliasService;
    private readonly string _connectionString;

    public CheckEtransferAliasAvailability(
        ILogger<CheckEtransferAliasAvailability> logger,
        IEtransferEmailAliasService aliasService)
    {
        _logger = logger;
        _aliasService = aliasService;

        _connectionString =
            Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing 'SqlConnectionString' env var.");
    }

    [Function("CheckEtransferAliasAvailability")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,             "get",
            Route = "organization/{orgID:int}/etransfer-alias/availability")]
        HttpRequestData req,
        int orgID)
    {
        try
        {
            var query =
                HttpUtility.ParseQueryString(
                    req.Url.Query);

            var prefix =
                EtransferAliasAddress.NormalizePrefix(
                    query["prefix"]);

            var emailAddress =
                EtransferAliasAddress.BuildEmailAddress(
                    prefix);

            await using var connection =
                new SqlConnection(
                    _connectionString);

            await connection.OpenAsync(
                req.FunctionContext.CancellationToken);

            var currentEmailAddress =
                await GetCurrentOrganizationEmailAsync(
                    connection,
                    orgID,
                    req.FunctionContext.CancellationToken);

            if (currentEmailAddress.OrganizationNotFound)
            {
                var notFound =
                    req.CreateResponse(
                        HttpStatusCode.NotFound);

                await notFound.WriteAsJsonAsync(new
                {
                    checkSucceeded = false,
                    isAvailable = false,
                    prefix,
                    emailAddress,
                    message = "Organization not found."
                });

                return notFound;
            }

            if (string.Equals(
                    NormalizeOptionalEmail(
                        currentEmailAddress.EmailAddress),
                    emailAddress,
                    StringComparison.Ordinal))
            {
                var current =
                    req.CreateResponse(
                        HttpStatusCode.OK);

                await current.WriteAsJsonAsync(new
                {
                    checkSucceeded = true,
                    isAvailable = true,
                    isCurrentOrganizationAlias = true,
                    prefix,
                    emailAddress,
                    message = $"{emailAddress} is already assigned to this organization."
                });

                return current;
            }

            if (await IsUsedByAnotherOrganizationAsync(
                    connection,
                    orgID,
                    emailAddress,
                    req.FunctionContext.CancellationToken))
            {
                return await CreateAvailabilityResponseAsync(
                    req,
                    prefix,
                    emailAddress,
                    isAvailable: false);
            }

            var existsInExchange =
                await _aliasService.AliasExistsAsync(
                    emailAddress,
                    req.FunctionContext.CancellationToken);

            return await CreateAvailabilityResponseAsync(
                req,
                prefix,
                emailAddress,
                isAvailable: !existsInExchange);
        }
        catch (ArgumentException exception)
        {
            var bad =
                req.CreateResponse(
                    HttpStatusCode.BadRequest);

            await bad.WriteAsJsonAsync(new
            {
                checkSucceeded = false,
                isAvailable = false,
                message = exception.Message
            });

            return bad;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error checking e-transfer alias availability for OrganizationID {OrganizationID}.",
                orgID);

            var unavailable =
                req.CreateResponse(
                    HttpStatusCode.ServiceUnavailable);

            await unavailable.WriteAsJsonAsync(new
            {
                checkSucceeded = false,
                isAvailable = false,
                message = "The email availability check is temporarily unavailable. Please try again."
            });

            return unavailable;
        }
    }

    private static async Task<HttpResponseData>
        CreateAvailabilityResponseAsync(
            HttpRequestData req,
            string prefix,
            string emailAddress,
            bool isAvailable)
    {
        var response =
            req.CreateResponse(
                HttpStatusCode.OK);

        await response.WriteAsJsonAsync(new
        {
            checkSucceeded = true,
            isAvailable,
            isCurrentOrganizationAlias = false,
            prefix,
            emailAddress,
            message = isAvailable
                ? $"{emailAddress} is available. Press Save to use it."
                : $"{emailAddress} is already in use. Please choose another email prefix."
        });

        return response;
    }

    private static async Task<(bool OrganizationNotFound, string? EmailAddress)>
        GetCurrentOrganizationEmailAsync(
            SqlConnection connection,
            int organizationID,
            CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EtransferEmailAddress
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
                organizationID;

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(
                cancellationToken))
        {
            return (true, null);
        }

        return reader.IsDBNull(0)
            ? (false, null)
            : (false, reader.GetString(0));
    }

    private static async Task<bool>
        IsUsedByAnotherOrganizationAsync(
            SqlConnection connection,
            int organizationID,
            string emailAddress,
            CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP (1) 1
            FROM dbo.Organization
            WHERE EMAOrganizationID <> @EMAOrganizationID
              AND LOWER(LTRIM(RTRIM(EtransferEmailAddress))) = @EtransferEmailAddress;
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
                organizationID;

        command.Parameters
            .Add(
                "@EtransferEmailAddress",
                SqlDbType.VarChar,
                255)
            .Value =
                emailAddress;

        var result =
            await command.ExecuteScalarAsync(
                cancellationToken);

        return result is not null;
    }

    private static string? NormalizeOptionalEmail(
        string? emailAddress) =>
        string.IsNullOrWhiteSpace(emailAddress)
            ? null
            : emailAddress.Trim().ToLowerInvariant();
}
