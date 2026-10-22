using System.Data;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using EMA.Api.Services.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Donations;

public sealed class DonorDirectedChanges
{
    private const int EditorLoadedStepID = 105;
    private const int EditorSavedStepID = 110;
    private const int MaximumDistributions = 50;
    private const decimal MaximumDistributionAmount = 99_999_999.99m;

    private static readonly HashSet<string>
        AnonymousNameProperties =
        new(
            StringComparer.OrdinalIgnoreCase)
        {
            "OfficialDonorName",
            "DonorName",
            "InteracDonorName"
        };

    private static readonly HashSet<string>
        AnonymousContactProperties =
        new(
            StringComparer.OrdinalIgnoreCase)
        {
            "Address",
            "City",
            "ProvinceStateCode",
            "PostalCode",
            "EmailAddress",
            "DonorEmail",
            "PhoneNumber"
        };

    private readonly ILogger<DonorDirectedChanges>
        _logger;

    private readonly IDonorAccessTokenService
        _donorAccessTokenService;

    private readonly string _connectionString;

    public DonorDirectedChanges(
        ILogger<DonorDirectedChanges> logger,
        IDonorAccessTokenService donorAccessTokenService)
    {
        _logger =
            logger;

        _donorAccessTokenService =
            donorAccessTokenService;

        _connectionString =
            Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString environment variable.");
    }

    [Function("GetDonorDirectedChanges")]
    public async Task<IActionResult> GetAsync(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "donor-directed-changes/{donationID:long}")]
        HttpRequest request,
        long donationID)
    {
        if (!TryAuthorize(
                request,
                donationID,
                out var identity))
        {
            return Unauthorized();
        }

        try
        {
            await using var connection =
                new SqlConnection(
                    _connectionString);

            await connection.OpenAsync(
                request.HttpContext.RequestAborted);

            var response =
                await LoadDonationAsync(
                    connection,
                    donationID,
                    identity!,
                    request.HttpContext.RequestAborted);

            if (response is null)
            {
                return new NotFoundObjectResult(
                    new
                    {
                        message =
                            "The donation was not found."
                    });
            }

            if (!response.IsAnonymous &&
                response.Donor.EMADonorID !=
                identity!.DonorID)
            {
                return Unauthorized();
            }

            await LogStepAsync(
                connection,
                donationID,
                EditorLoadedStepID,
                request.HttpContext.RequestAborted);

            response.TributeTypes =
                await LoadTributeTypesAsync(
                    connection,
                    identity!.EMAOrganizationID,
                    response.Donation.EMATributeTypeID,
                    request.HttpContext.RequestAborted);

            response.Campuses =
                await LoadCampusesAsync(
                    connection,
                    identity.EMAOrganizationID,
                    donationID,
                    request.HttpContext.RequestAborted);

            response.Funds =
                await LoadFundsAsync(
                    connection,
                    identity.EMAOrganizationID,
                    donationID,
                    request.HttpContext.RequestAborted);

            response.Distributions =
                await LoadDistributionsAsync(
                    connection,
                    donationID,
                    request.HttpContext.RequestAborted);

            response.Steps =
                await LoadStepsAsync(
                    connection,
                    donationID,
                    request.HttpContext.RequestAborted);

            response.Audits =
                await LoadAuditsAsync(
                    connection,
                    donationID,
                    response.IsAnonymous,
                    request.HttpContext.RequestAborted);

            return new OkObjectResult(
                response);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Unable to load donor-directed changes for donation {DonationID}.",
                donationID);

            return new ObjectResult(
                new
                {
                    message =
                        "Unable to load the donation."
                })
            {
                StatusCode =
                    StatusCodes.Status500InternalServerError
            };
        }
    }

    [Function("SaveDonorDirectedChanges")]
    public async Task<IActionResult> SaveAsync(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "post",
            Route = "donor-directed-changes/{donationID:long}")]
        HttpRequest request,
        long donationID)
    {
        if (!TryAuthorize(
                request,
                donationID,
                out var identity))
        {
            return Unauthorized();
        }

        DonorDirectedChangesSaveRequest? payload;

        try
        {
            payload =
                await JsonSerializer.DeserializeAsync<
                    DonorDirectedChangesSaveRequest>(
                    request.Body,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive =
                            true
                    },
                    request.HttpContext.RequestAborted);
        }
        catch (JsonException)
        {
            return BadRequest(
                "The request body is not valid JSON.");
        }

        var validationMessage =
            ValidatePayload(
                payload);

        if (validationMessage is not null)
        {
            return BadRequest(
                validationMessage);
        }

        try
        {
            var distributionJson =
                JsonSerializer.Serialize(
                    payload!.Distributions);

            await using var connection =
                new SqlConnection(
                    _connectionString);

            await connection.OpenAsync(
                request.HttpContext.RequestAborted);

            await using var command =
                new SqlCommand(
                    "dbo.sp_DonorDirectedChangesSave",
                    connection)
                {
                    CommandType =
                        CommandType.StoredProcedure
                };

            command.Parameters.Add(
                    "@EMAOrganizationID",
                    SqlDbType.Int)
                .Value =
                    identity!.EMAOrganizationID;

            command.Parameters.Add(
                    "@EMADonationID",
                    SqlDbType.BigInt)
                .Value =
                    donationID;

            command.Parameters.Add(
                    "@TokenDonorID",
                    SqlDbType.Int)
                .Value =
                    identity.DonorID;

            AddNullableString(
                command,
                "@OfficialDonorName",
                SqlDbType.NVarChar,
                510,
                payload.OfficialDonorName);

            AddNullableString(
                command,
                "@Address",
                SqlDbType.NVarChar,
                510,
                payload.Address);

            AddNullableString(
                command,
                "@City",
                SqlDbType.NVarChar,
                255,
                payload.City);

            AddNullableString(
                command,
                "@ProvinceStateCode",
                SqlDbType.VarChar,
                32,
                payload.ProvinceStateCode);

            AddNullableString(
                command,
                "@PostalCode",
                SqlDbType.VarChar,
                32,
                payload.PostalCode);

            AddNullableString(
                command,
                "@Country",
                SqlDbType.NVarChar,
                255,
                payload.Country);

            AddNullableString(
                command,
                "@PhoneNumber",
                SqlDbType.VarChar,
                64,
                payload.PhoneNumber);

            AddNullableString(
                command,
                "@Comments",
                SqlDbType.NVarChar,
                -1,
                payload.Comments);

            command.Parameters.Add(
                    "@EMATributeTypeID",
                    SqlDbType.Int)
                .Value =
                    (object?)payload.EMATributeTypeID
                    ?? DBNull.Value;

            AddNullableString(
                command,
                "@TributeText",
                SqlDbType.NVarChar,
                -1,
                payload.TributeText);

            command.Parameters.Add(
                    "@IsTemplate",
                    SqlDbType.Char,
                    1)
                .Value =
                    payload.IsTemplate
                        ? "Y"
                        : "N";

            command.Parameters.Add(
                    "@MakeAnonymous",
                    SqlDbType.Bit)
                .Value =
                    payload.MakeAnonymous;

            command.Parameters.Add(
                    "@DistributionsJson",
                    SqlDbType.NVarChar,
                    -1)
                .Value =
                    distributionJson;

            await command.ExecuteNonQueryAsync(
                request.HttpContext.RequestAborted);

            return new OkObjectResult(
                new
                {
                    message =
                        "Your changes have been recorded.",

                    isAnonymous =
                        payload.MakeAnonymous
                });
        }
        catch (SqlException exception)
            when (exception.Number is >= 52100 and <= 52199)
        {
            return BadRequest(
                exception.Message);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Unable to save donor-directed changes for donation {DonationID}.",
                donationID);

            return new ObjectResult(
                new
                {
                    message =
                        "Unable to save the donation changes."
                })
            {
                StatusCode =
                    StatusCodes.Status500InternalServerError
            };
        }
    }

    private bool TryAuthorize(
        HttpRequest request,
        long donationID,
        out DonorAccessIdentity? identity)
    {
        identity = null;

        var authorization =
            request.Headers.Authorization
                .FirstOrDefault();

        const string bearerPrefix =
            "Bearer ";

        if (string.IsNullOrWhiteSpace(
                authorization) ||
            !authorization.StartsWith(
                bearerPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var token =
            authorization[
                bearerPrefix.Length..]
                .Trim();

        return _donorAccessTokenService
            .TryValidateDonationChangesToken(
                token,
                donationID,
                out identity);
    }

    private static UnauthorizedObjectResult
        Unauthorized()
    {
        return new UnauthorizedObjectResult(
            new
            {
                message =
                    "The donation-change link is invalid or expired."
            });
    }

    private static BadRequestObjectResult BadRequest(
        string message)
    {
        return new BadRequestObjectResult(
            new
            {
                message
            });
    }

    private static string? ValidatePayload(
        DonorDirectedChangesSaveRequest? payload)
    {
        if (payload is null)
        {
            return "The request body is required.";
        }

        if (payload.Distributions.Count == 0)
        {
            return "At least one fund allocation is required.";
        }

        if (payload.Distributions.Count >
            MaximumDistributions)
        {
            return $"No more than {MaximumDistributions} fund allocations are allowed.";
        }

        if (payload.Distributions.Any(
                distribution =>
                    distribution.DistributionAmount <= 0 ||
                    distribution.DistributionAmount >
                    MaximumDistributionAmount))
        {
            return "Each fund allocation must contain a valid positive amount.";
        }

        if (payload.Distributions
            .GroupBy(
                distribution =>
                    new
                    {
                        distribution.EMACampusID,
                        distribution.EMAFundID
                    })
            .Any(group =>
                group.Count() > 1))
        {
            return "Duplicate fund allocations are not allowed.";
        }

        return null;
    }

    private static async Task<
        DonorDirectedChangesResponse?> LoadDonationAsync(
        SqlConnection connection,
        long donationID,
        DonorAccessIdentity identity,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                d.EMADonationID,
                d.EMADonorID,
                d.ReferenceNumber,
                d.DateTimeReceived,
                d.Comments,
                d.TributeText,
                d.EMATributeTypeID,
                d.DonationAmount,
                d.IsTemplate,
                d.DeadlineToMakeFundChanges,
                donor.OfficialDonorName,
                donor.InteracDonorName,
                donor.EmailAddress,
                donor.Address,
                donor.City,
                donor.ProvinceStateCode,
                donor.PostalCode,
                donor.Country,
                donor.PhoneNumber,
                organization.OrganizationName,
                organization.AdminEmail,
                organization.AdminPhone,
                organization.UseCampuses,
                organization.UseFunds,
                organization.UseTributeTypes,
                organization.UseAnonymous,
                organization.EMADonorIDForAnonymous,
                organization.DateFormatID,
                organization.MessageAboutAnonymousDonations
            FROM dbo.Donation AS d
            INNER JOIN dbo.Organization AS organization
                ON organization.EMAOrganizationID =
                   d.EMAOrganizationID
            INNER JOIN dbo.Donor AS donor
                ON donor.EMADonorID = d.EMADonorID
               AND donor.EMAOrganizationID =
                   d.EMAOrganizationID
            WHERE d.EMADonationID = @DonationID
              AND d.EMAOrganizationID = @OrganizationID;
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters.Add(
                "@DonationID",
                SqlDbType.BigInt)
            .Value =
                donationID;

        command.Parameters.Add(
                "@OrganizationID",
                SqlDbType.Int)
            .Value =
                identity.EMAOrganizationID;

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(
                cancellationToken))
        {
            return null;
        }

        var donorID =
            GetInt32(
                reader,
                "EMADonorID");

        var anonymousDonorID =
            GetNullableInt32(
                reader,
                "EMADonorIDForAnonymous");

        var isAnonymous =
            anonymousDonorID.HasValue &&
            donorID == anonymousDonorID.Value;

        var lockDateTime =
            GetNullableDateTime(
                reader,
                "DeadlineToMakeFundChanges");

        return new DonorDirectedChangesResponse
        {
            OrganizationName =
                GetString(
                    reader,
                    "OrganizationName")
                ?? string.Empty,

            AdminEmail =
                GetString(
                    reader,
                    "AdminEmail"),

            AdminPhone =
                GetString(
                    reader,
                    "AdminPhone"),

            UseCampuses =
                IsYes(
                    reader,
                    "UseCampuses"),

            UseFunds =
                IsYes(
                    reader,
                    "UseFunds"),

            UseTributeTypes =
                IsYes(
                    reader,
                    "UseTributeTypes"),

            UseAnonymous =
                IsYes(
                    reader,
                    "UseAnonymous"),

            AnonymousDonorID =
                anonymousDonorID,

            AnonymousMessage =
                GetString(
                    reader,
                    "MessageAboutAnonymousDonations")
                ?? string.Empty,

            DateFormat =
                GetString(
                    reader,
                    "DateFormatID")
                ?? "yyyy-MM-dd",

            IsAnonymous =
                isAnonymous,

            IsPastDeadline =
                lockDateTime.HasValue &&
                DateTime.UtcNow >
                lockDateTime.Value,

            LockDateTime =
                lockDateTime,

            Donor =
                new DonorResponse
                {
                    EMADonorID =
                        donorID,

                    OfficialDonorName =
                        isAnonymous
                            ? "Hidden - Anonymous"
                            : GetString(
                                reader,
                                "OfficialDonorName"),

                    InteracDonorName =
                        isAnonymous
                            ? "Hidden - Anonymous"
                            : GetString(
                                reader,
                                "InteracDonorName"),

                    EmailAddress =
                        isAnonymous
                            ? string.Empty
                            : GetString(
                                reader,
                                "EmailAddress"),

                    Address =
                        isAnonymous
                            ? string.Empty
                            : GetString(
                                reader,
                                "Address"),

                    City =
                        isAnonymous
                            ? string.Empty
                            : GetString(
                                reader,
                                "City"),

                    ProvinceStateCode =
                        isAnonymous
                            ? string.Empty
                            : GetString(
                                reader,
                                "ProvinceStateCode"),

                    PostalCode =
                        isAnonymous
                            ? string.Empty
                            : GetString(
                                reader,
                                "PostalCode"),

                    Country =
                        isAnonymous
                            ? string.Empty
                            : GetString(
                                reader,
                                "Country"),

                    PhoneNumber =
                        isAnonymous
                            ? string.Empty
                            : GetString(
                                reader,
                                "PhoneNumber")
                },

            Donation =
                new DonationResponse
                {
                    ReferenceNumber =
                        GetString(
                            reader,
                            "ReferenceNumber"),

                    DateTimeReceived =
                        GetNullableDateTime(
                            reader,
                            "DateTimeReceived"),

                    Comments =
                        GetString(
                            reader,
                            "Comments"),

                    TributeText =
                        GetString(
                            reader,
                            "TributeText"),

                    EMATributeTypeID =
                        GetNullableInt32(
                            reader,
                            "EMATributeTypeID"),

                    DonationAmount =
                        GetDecimal(
                            reader,
                            "DonationAmount"),

                    IsTemplate =
                        string.Equals(
                            GetString(
                                reader,
                                "IsTemplate"),
                            "Y",
                            StringComparison.OrdinalIgnoreCase)
                }
        };
    }

    private static async Task<List<LookupResponse>>
        LoadTributeTypesAsync(
        SqlConnection connection,
        int organizationID,
        int? selectedID,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                EMATributeTypeID,
                Description
            FROM dbo.OrganizationTributeType
            WHERE EMAOrganizationID = @OrganizationID
              AND
              (
                  IsActive = 'Y'
                  OR EMATributeTypeID = @SelectedID
              )
            ORDER BY Sequence, Description;
            """;

        return await LoadLookupsAsync(
            connection,
            sql,
            organizationID,
            selectedID,
            cancellationToken);
    }

    private static async Task<List<LookupResponse>>
        LoadCampusesAsync(
        SqlConnection connection,
        int organizationID,
        long donationID,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                campus.EMACampusID,
                campus.Name
            FROM dbo.OrganizationCampus AS campus
            WHERE campus.EMAOrganizationID = @OrganizationID
              AND
              (
                  campus.IsActive = 'Y'
                  OR EXISTS
                  (
                      SELECT 1
                      FROM dbo.DonationDistribution AS distribution
                      WHERE distribution.EMADonationID = @DonationID
                        AND distribution.EMACampusID =
                            campus.EMACampusID
                  )
              )
            ORDER BY campus.Sequence, campus.Name;
            """;

        return await LoadLookupsForDonationAsync(
            connection,
            sql,
            organizationID,
            donationID,
            cancellationToken);
    }

    private static async Task<List<LookupResponse>>
        LoadFundsAsync(
        SqlConnection connection,
        int organizationID,
        long donationID,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                fund.EMAFundID,
                fund.Name
            FROM dbo.OrganizationFund AS fund
            WHERE fund.EMAOrganizationID = @OrganizationID
              AND
              (
                  fund.IsActive = 'Y'
                  OR EXISTS
                  (
                      SELECT 1
                      FROM dbo.DonationDistribution AS distribution
                      WHERE distribution.EMADonationID = @DonationID
                        AND distribution.EMAFundID =
                            fund.EMAFundID
                  )
              )
            ORDER BY fund.Sequence, fund.Name;
            """;

        return await LoadLookupsForDonationAsync(
            connection,
            sql,
            organizationID,
            donationID,
            cancellationToken);
    }

    private static async Task<List<LookupResponse>>
        LoadLookupsAsync(
        SqlConnection connection,
        string sql,
        int organizationID,
        int? selectedID,
        CancellationToken cancellationToken)
    {
        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters.Add(
                "@OrganizationID",
                SqlDbType.Int)
            .Value =
                organizationID;

        command.Parameters.Add(
                "@SelectedID",
                SqlDbType.Int)
            .Value =
                (object?)selectedID
                ?? DBNull.Value;

        return await ReadLookupsAsync(
            command,
            cancellationToken);
    }

    private static async Task<List<LookupResponse>>
        LoadLookupsForDonationAsync(
        SqlConnection connection,
        string sql,
        int organizationID,
        long donationID,
        CancellationToken cancellationToken)
    {
        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters.Add(
                "@OrganizationID",
                SqlDbType.Int)
            .Value =
                organizationID;

        command.Parameters.Add(
                "@DonationID",
                SqlDbType.BigInt)
            .Value =
                donationID;

        return await ReadLookupsAsync(
            command,
            cancellationToken);
    }

    private static async Task<List<LookupResponse>>
        ReadLookupsAsync(
        SqlCommand command,
        CancellationToken cancellationToken)
    {
        var results =
            new List<LookupResponse>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            results.Add(
                new LookupResponse
                {
                    ID =
                        reader.GetInt32(
                            0),

                    Description =
                        reader.IsDBNull(
                            1)
                            ? string.Empty
                            : reader.GetString(
                                1)
                });
        }

        return results;
    }

    private static async Task<List<DistributionResponse>>
        LoadDistributionsAsync(
        SqlConnection connection,
        long donationID,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                EMACampusID,
                EMAFundID,
                DistributionAmount
            FROM dbo.DonationDistribution
            WHERE EMADonationID = @DonationID
            ORDER BY EMADonationDistributionID;
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters.Add(
                "@DonationID",
                SqlDbType.BigInt)
            .Value =
                donationID;

        var results =
            new List<DistributionResponse>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            results.Add(
                new DistributionResponse
                {
                    EMACampusID =
                        reader.IsDBNull(
                            0)
                            ? null
                            : reader.GetInt32(
                                0),

                    EMAFundID =
                        reader.IsDBNull(
                            1)
                            ? null
                            : reader.GetInt32(
                                1),

                    DistributionAmount =
                        reader.IsDBNull(
                            2)
                            ? 0
                            : Convert.ToDecimal(
                                reader.GetValue(
                                    2))
                });
        }

        return results;
    }

    private static async Task<List<StepResponse>>
        LoadStepsAsync(
        SqlConnection connection,
        long donationID,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                log.DonationStepID,
                step.Description,
                log.DateTimeLogged
            FROM dbo.DonationStepLog AS log
            LEFT JOIN dbo.DonationStep AS step
                ON step.DonationStepID =
                   log.DonationStepID
            WHERE log.EMADonationID = @DonationID
            ORDER BY log.DateTimeLogged;
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters.Add(
                "@DonationID",
                SqlDbType.BigInt)
            .Value =
                donationID;

        var results =
            new List<StepResponse>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            results.Add(
                new StepResponse
                {
                    DonationStepID =
                        reader.GetInt32(
                            0),

                    Description =
                        reader.IsDBNull(
                            1)
                            ? string.Empty
                            : reader.GetString(
                                1),

                    DateTimeLogged =
                        reader.IsDBNull(
                            2)
                            ? null
                            : reader.GetDateTime(
                                2)
                });
        }

        return results;
    }

    private static async Task<List<AuditResponse>>
        LoadAuditsAsync(
        SqlConnection connection,
        long donationID,
        bool isAnonymous,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                audit.DateTimeCreated,
                audit.DDDDJsonPayload
            FROM dbo.DonationAudit AS audit
            WHERE audit.EMADonationID = @DonationID
            ORDER BY audit.DateTimeCreated DESC;
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters.Add(
                "@DonationID",
                SqlDbType.BigInt)
            .Value =
                donationID;

        var results =
            new List<AuditResponse>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            var payload =
                reader.IsDBNull(
                    1)
                    ? null
                    : reader.GetString(
                        1);

            results.Add(
                new AuditResponse
                {
                    DateTimeCreated =
                        reader.IsDBNull(
                            0)
                            ? null
                            : reader.GetDateTime(
                                0),

                    UserName =
                        "Donor",

                    Payload =
                        isAnonymous
                            ? MaskAnonymousAuditPayload(
                                payload)
                            : payload
                });
        }

        return results;
    }

    private static async Task LogStepAsync(
        SqlConnection connection,
        long donationID,
        int stepID,
        CancellationToken cancellationToken)
    {
        const string sql = """
            IF EXISTS
            (
                SELECT 1
                FROM dbo.DonationStep
                WHERE DonationStepID = @StepID
            )
            AND NOT EXISTS
            (
                SELECT 1
                FROM dbo.DonationStepLog
                WHERE EMADonationID = @DonationID
                  AND DonationStepID = @StepID
            )
            BEGIN
                INSERT INTO dbo.DonationStepLog
                (
                    EMADonationID,
                    DonationStepID,
                    DateTimeLogged
                )
                VALUES
                (
                    @DonationID,
                    @StepID,
                    SYSUTCDATETIME()
                );
            END;
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters.Add(
                "@DonationID",
                SqlDbType.BigInt)
            .Value =
                donationID;

        command.Parameters.Add(
                "@StepID",
                SqlDbType.Int)
            .Value =
                stepID;

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static string? MaskAnonymousAuditPayload(
        string? payload)
    {
        if (string.IsNullOrWhiteSpace(
                payload))
        {
            return payload;
        }

        try
        {
            var node =
                JsonNode.Parse(
                    payload);

            MaskAnonymousNode(
                node);

            return node?.ToJsonString();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void MaskAnonymousNode(
        JsonNode? node)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var property in
                     jsonObject.ToList())
            {
                if (AnonymousNameProperties.Contains(
                        property.Key))
                {
                    jsonObject[property.Key] =
                        "Hidden - Anonymous";
                }
                else if (AnonymousContactProperties.Contains(
                             property.Key))
                {
                    jsonObject[property.Key] =
                        string.Empty;
                }
                else
                {
                    MaskAnonymousNode(
                        property.Value);
                }
            }
        }
        else if (node is JsonArray jsonArray)
        {
            foreach (var item in
                     jsonArray)
            {
                MaskAnonymousNode(
                    item);
            }
        }
    }

    private static void AddNullableString(
        SqlCommand command,
        string name,
        SqlDbType type,
        int size,
        string? value)
    {
        var parameter =
            command.Parameters.Add(
                name,
                type,
                size);

        parameter.Value =
            string.IsNullOrWhiteSpace(
                value)
                ? DBNull.Value
                : value.Trim();
    }

    private static string? GetString(
        SqlDataReader reader,
        string name)
    {
        var ordinal =
            reader.GetOrdinal(
                name);

        return reader.IsDBNull(
                ordinal)
            ? null
            : reader.GetValue(
                    ordinal)
                ?.ToString();
    }

    private static int GetInt32(
        SqlDataReader reader,
        string name)
    {
        var ordinal =
            reader.GetOrdinal(
                name);

        return reader.IsDBNull(
                ordinal)
            ? 0
            : Convert.ToInt32(
                reader.GetValue(
                    ordinal));
    }

    private static int? GetNullableInt32(
        SqlDataReader reader,
        string name)
    {
        var ordinal =
            reader.GetOrdinal(
                name);

        return reader.IsDBNull(
                ordinal)
            ? null
            : Convert.ToInt32(
                reader.GetValue(
                    ordinal));
    }

    private static DateTime? GetNullableDateTime(
        SqlDataReader reader,
        string name)
    {
        var ordinal =
            reader.GetOrdinal(
                name);

        return reader.IsDBNull(
                ordinal)
            ? null
            : reader.GetDateTime(
                ordinal);
    }

    private static decimal GetDecimal(
        SqlDataReader reader,
        string name)
    {
        var ordinal =
            reader.GetOrdinal(
                name);

        return reader.IsDBNull(
                ordinal)
            ? 0
            : Convert.ToDecimal(
                reader.GetValue(
                    ordinal));
    }

    private static bool IsYes(
        SqlDataReader reader,
        string name)
    {
        return string.Equals(
            GetString(
                reader,
                name),
            "Y",
            StringComparison.OrdinalIgnoreCase);
    }

    public sealed class DonorDirectedChangesSaveRequest
    {
        public string? OfficialDonorName { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? ProvinceStateCode { get; set; }
        public string? PostalCode { get; set; }
        public string? Country { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Comments { get; set; }
        public int? EMATributeTypeID { get; set; }
        public string? TributeText { get; set; }
        public bool IsTemplate { get; set; }
        public bool MakeAnonymous { get; set; }

        public List<DistributionResponse> Distributions
        {
            get;
            set;
        } = [];
    }

    public sealed class DonorDirectedChangesResponse
    {
        public string OrganizationName { get; set; } =
            string.Empty;

        public string? AdminEmail { get; set; }
        public string? AdminPhone { get; set; }
        public bool UseCampuses { get; set; }
        public bool UseFunds { get; set; }
        public bool UseTributeTypes { get; set; }
        public bool UseAnonymous { get; set; }
        public int? AnonymousDonorID { get; set; }
        public string AnonymousMessage { get; set; } =
            string.Empty;

        public string DateFormat { get; set; } =
            "yyyy-MM-dd";

        public bool IsAnonymous { get; set; }
        public bool IsPastDeadline { get; set; }
        public DateTime? LockDateTime { get; set; }

        public DonorResponse Donor { get; set; } =
            new();

        public DonationResponse Donation { get; set; } =
            new();

        public List<LookupResponse> TributeTypes { get; set; } =
            [];

        public List<LookupResponse> Campuses { get; set; } =
            [];

        public List<LookupResponse> Funds { get; set; } =
            [];

        public List<DistributionResponse> Distributions { get; set; } =
            [];

        public List<StepResponse> Steps { get; set; } =
            [];

        public List<AuditResponse> Audits { get; set; } =
            [];
    }

    public sealed class DonorResponse
    {
        public int EMADonorID { get; set; }
        public string? OfficialDonorName { get; set; }
        public string? InteracDonorName { get; set; }
        public string? EmailAddress { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? ProvinceStateCode { get; set; }
        public string? PostalCode { get; set; }
        public string? Country { get; set; }
        public string? PhoneNumber { get; set; }
    }

    public sealed class DonationResponse
    {
        public string? ReferenceNumber { get; set; }
        public DateTime? DateTimeReceived { get; set; }
        public string? Comments { get; set; }
        public string? TributeText { get; set; }
        public int? EMATributeTypeID { get; set; }
        public decimal DonationAmount { get; set; }
        public bool IsTemplate { get; set; }
    }

    public sealed class LookupResponse
    {
        public int ID { get; set; }
        public string Description { get; set; } =
            string.Empty;
    }

    public sealed class DistributionResponse
    {
        public int? EMACampusID { get; set; }
        public int? EMAFundID { get; set; }
        public decimal DistributionAmount { get; set; }
    }

    public sealed class StepResponse
    {
        public int DonationStepID { get; set; }
        public string Description { get; set; } =
            string.Empty;

        public DateTime? DateTimeLogged { get; set; }
    }

    public sealed class AuditResponse
    {
        public DateTime? DateTimeCreated { get; set; }
        public string UserName { get; set; } =
            string.Empty;

        public string? Payload { get; set; }
    }
}
