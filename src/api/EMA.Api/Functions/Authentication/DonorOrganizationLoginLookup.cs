using System.Data;
using System.Text.Json;
using EMA.Api.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.Authentication;

public sealed class DonorOrganizationLoginLookup
{
    private readonly ILogger<DonorOrganizationLoginLookup> _logger;
    private readonly string _connectionString;
    private readonly int _platformOrganizationID;

    public DonorOrganizationLoginLookup(
        ILogger<DonorOrganizationLoginLookup> logger,
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

    [Function("DonorOrganizationLoginLookup")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,             "post",
            Route = "donorlogin/{EMAOrganizationID:int}/lookup")]
        HttpRequest request,
        int EMAOrganizationID)
    {
        DonorOrganizationLoginLookupRequestDto? lookupRequest;

        try
        {
            lookupRequest =
                await JsonSerializer.DeserializeAsync<DonorOrganizationLoginLookupRequestDto>(
                    request.Body,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult(new
            {
                message =
                    "Invalid donor login lookup request."
            });
        }

        if (EMAOrganizationID <= 0)
        {
            return new BadRequestObjectResult(new
            {
                message =
                    "Organization is required."
            });
        }

        var phoneDigits =
            DigitsOnly(
                lookupRequest?.PhoneNumber ?? string.Empty);

        if (phoneDigits.Length != 10)
        {
            return new BadRequestObjectResult(new
            {
                message =
                    "Please input a valid 10 digit phone number."
            });
        }

        try
        {
            await using var connection =
                new SqlConnection(
                    _connectionString);

            await connection.OpenAsync();

            const string sql = """
                SELECT TOP (1)
                    d.EMADonorID,
                    d.OfficialDonorName,
                    d.InteracDonorName,
                    d.PhoneNumber,
                    o.EMAOrganizationID,
                    o.OrganizationName
                FROM dbo.Donor d
                INNER JOIN dbo.Organization o
                    ON d.EMAOrganizationID =
                       o.EMAOrganizationID
                INNER JOIN dbo.[Role] donorRole
                    ON donorRole.RoleID =
                       o.DonorRoleID
                WHERE d.EMAOrganizationID = @EMAOrganizationID
                  AND o.EMAOrganizationID <>
                      @PlatformOrganizationID
                  AND ISNULL(
                          o.OrganizationStatusID,
                          -1) <> 0
                  AND ISNULL(
                          donorRole.IsActive,
                          'Y') = 'Y'
                  AND RIGHT(
                      REPLACE(
                          REPLACE(
                              REPLACE(
                                  REPLACE(
                                      REPLACE(
                                          REPLACE(
                                              ISNULL(
                                                  d.PhoneNumber,
                                                  ''
                                              ),
                                              '(',
                                              ''
                                          ),
                                          ')',
                                          ''
                                      ),
                                      '-',
                                      ''
                                  ),
                                  ' ',
                                  ''
                              ),
                              '.',
                              ''
                          ),
                          '+',
                          ''
                      ),
                      10
                  ) = @PhoneDigits
                ORDER BY
                    d.EMADonorID;
                """;

            await using var command =
                new SqlCommand(
                    sql,
                    connection);

            command.Parameters.Add(
                "@EMAOrganizationID",
                SqlDbType.Int)
                .Value = EMAOrganizationID;

            command.Parameters.Add(
                "@PlatformOrganizationID",
                SqlDbType.Int)
                .Value =
                    _platformOrganizationID;

            command.Parameters.Add(
                "@PhoneDigits",
                SqlDbType.NVarChar,
                10)
                .Value = phoneDigits;

            await using var reader =
                await command.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                return new NotFoundObjectResult(new
                {
                    message =
                        "That phone number was not found. Please change the number or create a new donor record."
                });
            }

            var response =
                new DonorOrganizationLoginLookupResponseDto
                {
                    EMAOrganizationID =
                        Convert.ToInt32(
                            reader["EMAOrganizationID"]),

                    OrganizationName =
                        reader["OrganizationName"]?.ToString()
                        ?? string.Empty,

                    DonorID =
                        Convert.ToInt32(
                            reader["EMADonorID"]),

                    DonorName =
                        GetDonorDisplayName(
                            reader),

                    PhoneNumber =
                        reader["PhoneNumber"]?.ToString()
                        ?? FormatPhoneNumber(
                            phoneDigits)
                };

            return new OkObjectResult(
                response);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Error looking up donor login for EMAOrganizationID {EMAOrganizationID}.",
                EMAOrganizationID);

            return new ObjectResult(new
            {
                message =
                    "Unable to look up that phone number."
            })
            {
                StatusCode =
                    StatusCodes.Status500InternalServerError
            };
        }
    }

    private static string GetDonorDisplayName(
        SqlDataReader reader)
    {
        var officialName =
            reader["OfficialDonorName"]?.ToString();

        if (!string.IsNullOrWhiteSpace(
                officialName))
        {
            return officialName;
        }

        var interacName =
            reader["InteracDonorName"]?.ToString();

        if (!string.IsNullOrWhiteSpace(
                interacName))
        {
            return interacName;
        }

        return "Donor";
    }

    private static string DigitsOnly(
        string value)
    {
        return new string(
            value
                .Where(char.IsDigit)
                .ToArray());
    }

    private static string FormatPhoneNumber(
        string phoneDigits)
    {
        if (phoneDigits.Length != 10)
        {
            return phoneDigits;
        }

        return $"{phoneDigits[..3]}-{phoneDigits.Substring(3, 3)}-{phoneDigits.Substring(6, 4)}";
    }
}
