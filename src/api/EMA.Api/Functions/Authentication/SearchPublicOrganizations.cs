using System.Data;
using EMA.Api.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Authentication;

public sealed class SearchPublicOrganizations
{
    private readonly ILogger<SearchPublicOrganizations> _logger;
    private readonly string _connectionString;
    private readonly int _platformOrganizationID;

    public SearchPublicOrganizations(
        ILogger<SearchPublicOrganizations> logger,
        ApiRequestBoundaryValidator authorization)
    {
        _logger =
            logger;

        _platformOrganizationID =
            authorization
                .PlatformOrganizationID;

        _connectionString =
            Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString.");
    }

    [Function("SearchPublicOrganizations")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,             "get",
            Route = "public/organizations/search")]
        HttpRequest request)
    {
        var term =
            request.Query["term"]
                .ToString()
                .Trim();

        if (term.Length < 2)
        {
            return new OkObjectResult(
                new List<PublicOrganizationLookupDto>());
        }

        try
        {
            await using var connection =
                new SqlConnection(
                    _connectionString);

            await connection.OpenAsync();

            const string sql = """
                SELECT TOP (20)
                    organization.EMAOrganizationID,
                    organization.OrganizationName
                FROM dbo.Organization organization
                INNER JOIN dbo.[Role] donorRole
                    ON donorRole.RoleID =
                       organization.DonorRoleID
                WHERE organization.OrganizationName
                      LIKE '%' + @Term + '%'
                  AND organization.EMAOrganizationID <>
                      @PlatformOrganizationID
                  AND ISNULL(
                          organization.OrganizationStatusID,
                          -1) <> 0
                  AND ISNULL(
                          donorRole.IsActive,
                          'Y') = 'Y'
                ORDER BY
                    organization.OrganizationName;
                """;

            await using var command =
                new SqlCommand(
                    sql,
                    connection);

            command.Parameters.Add(
                "@Term",
                SqlDbType.NVarChar,
                200)
                .Value = term;

            command.Parameters.Add(
                "@PlatformOrganizationID",
                SqlDbType.Int)
                .Value =
                    _platformOrganizationID;

            await using var reader =
                await command.ExecuteReaderAsync();

            var organizations =
                new List<PublicOrganizationLookupDto>();

            while (await reader.ReadAsync())
            {
                organizations.Add(
                    new PublicOrganizationLookupDto
                    {
                        EMAOrganizationID =
                            Convert.ToInt32(
                                reader["EMAOrganizationID"]),

                        OrganizationName =
                            reader["OrganizationName"]?.ToString()
                            ?? string.Empty
                    });
            }

            return new OkObjectResult(
                organizations);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error searching public organizations.");

            return new ObjectResult(new
            {
                message =
                    "Unable to search organizations."
            })
            {
                StatusCode =
                    StatusCodes.Status500InternalServerError
            };
        }
    }
}
