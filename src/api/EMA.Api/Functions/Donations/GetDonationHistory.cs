using System.Data;
using EMA.Api.Services.Authentication;
using EMA.Api.Services.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Donations;

public sealed class GetDonorHistory
{
    private readonly ILogger<GetDonorHistory>
        _logger;

    private readonly string _connectionString;

    private readonly IDonorAccessTokenService
        _donorAccessTokenService;

    public GetDonorHistory(
        ILogger<GetDonorHistory> logger,
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

    [Function("GetDonorHistory")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,             "get",
            Route = "donor-history")]
        HttpRequest request)
    {
        var bearerToken =
            GetBearerToken(
                request);

        var hasApiIdentity =
            ApiRequestIdentity
                .TryFromPrincipal(
                    request.HttpContext.User,
                    out var apiIdentity) &&
            apiIdentity is
            {
                ActorType:
                    ApiActorType.Donor,
                DonorID: not null
            };

        DonorAccessIdentity?
            linkIdentity =
                null;

        if (!hasApiIdentity &&
            string.IsNullOrWhiteSpace(
                bearerToken))
        {
            return new UnauthorizedObjectResult(
                new
                {
                    message =
                        "A donor access token is required."
                });
        }

        if (!hasApiIdentity &&
            !_donorAccessTokenService
                .TryValidateGivingHistoryToken(
                    bearerToken!,
                    out linkIdentity))
        {
            return new UnauthorizedObjectResult(
                new
                {
                    message =
                        "The donor access link or session is invalid or expired."
                });
        }

        if (!hasApiIdentity &&
            linkIdentity is null)
        {
            return new UnauthorizedObjectResult(
                new
                {
                    message =
                        "The donor identity could not be verified."
                });
        }

        var donorID =
            hasApiIdentity
                ? apiIdentity!.DonorID!.Value
                : linkIdentity!.DonorID;

        var organizationID =
            hasApiIdentity
                ? apiIdentity!
                    .EMAOrganizationID
                : linkIdentity!
                    .EMAOrganizationID;

        var selectedYear =
            DateTime.UtcNow.Year;

        var yearQueryValue =
            request.Query["year"]
                .FirstOrDefault();

        if (int.TryParse(
                yearQueryValue,
                out var parsedYear))
        {
            selectedYear =
                parsedYear;
        }

        if (selectedYear < 1900 ||
            selectedYear > 2100)
        {
            return new BadRequestObjectResult(
                new
                {
                    message =
                        "Invalid year."
                });
        }

        _logger.LogInformation(
            "GetDonorHistory requested for authenticated donor {DonorID}, organization {EMAOrganizationID}.",
            donorID,
            organizationID);

        try
        {
            await using var connection =
                new SqlConnection(
                    _connectionString);

            await connection.OpenAsync(
                request.HttpContext
                    .RequestAborted);

            var donorName =
                await GetDonorNameAsync(
                    connection,
                    donorID,
                    organizationID);

            if (donorName is null)
            {
                return new NotFoundObjectResult(
                    new
                    {
                        message =
                            "The donor record was not found."
                    });
            }

            var availableYears =
                await GetAvailableYearsAsync(
                    connection,
                    donorID,
                    organizationID);

            if (!availableYears.Contains(
                    selectedYear))
            {
                availableYears.Add(
                    selectedYear);

                availableYears =
                    availableYears
                        .Distinct()
                        .OrderByDescending(
                            year => year)
                        .ToList();
            }

            var donations =
                await GetDonationsForYearAsync(
                    connection,
                    donorID,
                    organizationID,
                    selectedYear);

            return new OkObjectResult(
                new DonorHistoryResponse
                {
                    DonorName =
                        donorName,

                    SelectedYear =
                        selectedYear,

                    AvailableYears =
                        availableYears,

                    Donations =
                        donations
                });
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error fetching donor history for authenticated donor {DonorID}, organization {EMAOrganizationID}.",
                donorID,
                organizationID);

            return new ObjectResult(
                new
                {
                    message =
                        "Unable to load donation history."
                })
            {
                StatusCode =
                    StatusCodes
                        .Status500InternalServerError
            };
        }
    }

    private static string? GetBearerToken(
        HttpRequest request)
    {
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
            return null;
        }

        var token =
            authorization[
                bearerPrefix.Length..]
                .Trim();

        return string.IsNullOrWhiteSpace(
            token)
            ? null
            : token;
    }

    private static async Task<string?>
        GetDonorNameAsync(
            SqlConnection connection,
            int donorID,
            int EMAOrganizationID)
    {
        const string sql = """
            SELECT TOP 1
                COALESCE(
                    NULLIF(LTRIM(RTRIM(OfficialDonorName)), ''),
                    NULLIF(LTRIM(RTRIM(InteracDonorName)), ''),
                    'Donor'
                )
            FROM dbo.Donor
            WHERE EMADonorID = @DonorID
              AND EMAOrganizationID = @EMAOrganizationID;
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters.Add(
            "@DonorID",
            SqlDbType.Int)
            .Value = donorID;

        command.Parameters.Add(
            "@EMAOrganizationID",
            SqlDbType.Int)
            .Value = EMAOrganizationID;

        var result =
            await command.ExecuteScalarAsync();

        return result is null ||
               result == DBNull.Value
            ? null
            : result.ToString();
    }

    private static async Task<List<int>>
        GetAvailableYearsAsync(
            SqlConnection connection,
            int donorID,
            int EMAOrganizationID)
    {
        const string sql = """
            SELECT DISTINCT
                YEAR(DateTimeReceived)
            FROM dbo.Donation
            WHERE EMADonorID = @DonorID
              AND EMAOrganizationID = @EMAOrganizationID
              AND DateTimeReceived IS NOT NULL
            ORDER BY YEAR(DateTimeReceived) DESC;
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters.Add(
            "@DonorID",
            SqlDbType.Int)
            .Value = donorID;

        command.Parameters.Add(
            "@EMAOrganizationID",
            SqlDbType.Int)
            .Value = EMAOrganizationID;

        var years =
            new List<int>();

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            years.Add(
                reader.GetInt32(
                    0));
        }

        return years;
    }

    private static async Task<
        List<DonorHistoryDonation>>
        GetDonationsForYearAsync(
            SqlConnection connection,
            int donorID,
            int EMAOrganizationID,
            int selectedYear)
    {
        var startDate =
            new DateTime(
                selectedYear,
                1,
                1);

        var endDate =
            startDate.AddYears(
                1);

        const string sql = """
            SELECT
                history.EMADonationID,
                history.DateTimeReceived,
                ISNULL(history.MethodName, 'Unknown') AS Method,
                ISNULL(history.FundName, '') AS Fund,
                history.DonationAmount
            FROM dbo.vw_Donations_Detail history
            INNER JOIN dbo.Donation donation
                ON donation.EMADonationID =
                   history.EMADonationID
            WHERE donation.EMADonorID = @DonorID
              AND donation.EMAOrganizationID = @EMAOrganizationID
              AND history.DateTimeReceived >= @StartDate
              AND history.DateTimeReceived < @EndDate
            ORDER BY
                history.DateTimeReceived ASC,
                history.EMADonationID ASC;
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection);

        command.Parameters.Add(
            "@DonorID",
            SqlDbType.Int)
            .Value = donorID;

        command.Parameters.Add(
            "@EMAOrganizationID",
            SqlDbType.Int)
            .Value = EMAOrganizationID;

        command.Parameters.Add(
            "@StartDate",
            SqlDbType.DateTime2)
            .Value = startDate;

        command.Parameters.Add(
            "@EndDate",
            SqlDbType.DateTime2)
            .Value = endDate;

        var donations =
            new List<DonorHistoryDonation>();

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var amountOrdinal =
                reader.GetOrdinal(
                    "DonationAmount");

            donations.Add(
                new DonorHistoryDonation
                {
                    DonationId =
                        reader.GetInt64(
                            reader.GetOrdinal(
                                "EMADonationID")),

                    DonationDate =
                        reader.GetDateTime(
                            reader.GetOrdinal(
                                "DateTimeReceived")),

                    Method =
                        reader["Method"]?.ToString()
                        ?? "Unknown",

                    Fund =
                        reader["Fund"]?.ToString()
                        ?? string.Empty,

                    Amount =
                        reader.IsDBNull(
                            amountOrdinal)
                            ? 0
                            : Convert.ToDecimal(
                                reader.GetValue(
                                    amountOrdinal))
                });
        }

        return donations;
    }

    public sealed class DonorHistoryResponse
    {
        public string DonorName { get; set; } =
            string.Empty;

        public int SelectedYear { get; set; }

        public List<int> AvailableYears { get; set; } =
            new();

        public List<DonorHistoryDonation> Donations
        {
            get;
            set;
        } = new();
    }

    public sealed class DonorHistoryDonation
    {
        public long DonationId { get; set; }

        public DateTime DonationDate { get; set; }

        public string Method { get; set; } =
            string.Empty;

        public string Fund { get; set; } =
            string.Empty;

        public decimal Amount { get; set; }
    }
}
