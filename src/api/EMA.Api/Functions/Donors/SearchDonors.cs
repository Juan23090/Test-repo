using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Text;

namespace EMA.Api.Functions.Donors;

public sealed class SearchDonors
{
    private readonly ILogger<SearchDonors> _logger;
    private readonly string _connectionString;

    public SearchDonors(ILogger<SearchDonors> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")
            ?? throw new InvalidOperationException("Missing SqlConnectionString.");
    }

    [Function("SearchDonors")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "donors/search/{orgID:int}")] HttpRequest request,
        int orgID)
    {
        _logger.LogInformation("SearchDonors triggered for org {OrgID}.", orgID);

        try
        {
            string? query = request.Query["q"];
            string? field = request.Query["field"];
            bool exact =
                bool.TryParse(
                    request.Query["exact"],
                    out var parsedExact) &&
                parsedExact;

            var donors = new List<Dictionary<string, object?>>();

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var sql = new StringBuilder("""
                SELECT TOP 10
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
                    CAST(1 AS bit) AS IsActive,
                    DateTimeCreated,
                    CreatedByUserID,
                    DateTimeUpdated,
                    -- Schema detail: 'UpdatedbyUserID' uses a lowercase 'by' in this table.
                    UpdatedbyUserID
                FROM dbo.Donor
                WHERE EMAOrganizationID = @OrgID
                """);

            var trimmedQuery = query?.Trim() ?? string.Empty;
            var searchTerms = Array.Empty<string>();

            if (trimmedQuery.Length > 0)
            {
                var fieldFilter = field?.ToLowerInvariant() switch
                {
                    "official" =>
                        exact
                            ? " AND OfficialDonorName = @ExactQuery"
                            : " AND OfficialDonorName LIKE @Query ESCAPE '\\'",
                    "interac" =>
                        exact
                            ? " AND InteracDonorName = @ExactQuery"
                            : " AND InteracDonorName LIKE @Query ESCAPE '\\'",
                    "email" =>
                        exact
                            ? " AND EmailAddress = @ExactQuery"
                            : " AND EmailAddress LIKE @Query ESCAPE '\\'",
                    "phone" =>
                        exact
                            ? " AND REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(PhoneNumber, ' ', ''), '-', ''), '(', ''), ')', ''), '+', '') = @NormalizedQuery"
                            : " AND PhoneNumber LIKE @Query ESCAPE '\\'",
                    _ => null
                };

                if (fieldFilter is not null)
                {
                    sql.Append(fieldFilter);
                }
                else
                {
                    searchTerms = trimmedQuery
                        .Split(
                            (char[]?)null,
                            StringSplitOptions.RemoveEmptyEntries |
                            StringSplitOptions.TrimEntries)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(10)
                        .ToArray();

                    for (var index = 0; index < searchTerms.Length; index++)
                    {
                        sql.Append($"""

                             AND
                             (
                                 COALESCE(OfficialDonorName, '') LIKE @Term{index} ESCAPE '\'
                                 OR COALESCE(InteracDonorName, '') LIKE @Term{index} ESCAPE '\'
                                 OR COALESCE(EmailAddress, '') LIKE @Term{index} ESCAPE '\'
                                 OR COALESCE(PhoneNumber, '') LIKE @Term{index} ESCAPE '\'
                                 OR COALESCE(Address, '') LIKE @Term{index} ESCAPE '\'
                                 OR COALESCE(City, '') LIKE @Term{index} ESCAPE '\'
                                 OR COALESCE(ProvinceStateCode, '') LIKE @Term{index} ESCAPE '\'
                                 OR COALESCE(PostalCode, '') LIKE @Term{index} ESCAPE '\'
                                 OR COALESCE(Country, '') LIKE @Term{index} ESCAPE '\'
                                 OR COALESCE(CONVERT(nvarchar(100), GivingNumber), '') LIKE @Term{index} ESCAPE '\'
                             )
                            """);
                    }
                }
            }

            sql.Append(" ORDER BY OfficialDonorName;");

            await using var command = new SqlCommand(sql.ToString(), connection);
            command.Parameters.Add("@OrgID", SqlDbType.Int).Value = orgID;

            if (trimmedQuery.Length > 0)
            {
                command.Parameters.Add("@Query", SqlDbType.NVarChar, 520).Value =
                    $"%{EscapeLikePattern(trimmedQuery)}%";
                command.Parameters.Add("@ExactQuery", SqlDbType.NVarChar, 255).Value =
                    trimmedQuery;
                command.Parameters.Add("@NormalizedQuery", SqlDbType.NVarChar, 100).Value =
                    new string(trimmedQuery.Where(char.IsDigit).ToArray());

                for (var index = 0; index < searchTerms.Length; index++)
                {
                    command.Parameters.Add($"@Term{index}", SqlDbType.NVarChar, 520).Value =
                        $"%{EscapeLikePattern(searchTerms[index])}%";
                }
            }

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var donor = new Dictionary<string, object?>();

                for (int index = 0; index < reader.FieldCount; index++)
                {
                    string columnName = reader.GetName(index);
                    donor[columnName] = reader.IsDBNull(index) ? null : reader.GetValue(index);
                }

                donors.Add(donor);
            }

            return new OkObjectResult(donors);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error searching donors for organization {OrgID}.", orgID);

            return new ObjectResult(new { message = "Unable to search donors." })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }

    private static string EscapeLikePattern(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal);
}

