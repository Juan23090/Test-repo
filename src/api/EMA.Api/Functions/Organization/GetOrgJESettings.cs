using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using EMA.Api.DTOs;

namespace EMA_Api.Functions.Organization;

public class GetOrgJESettings
{
    private readonly ILogger<GetOrgJESettings> _logger;
    private readonly string _connectionString;

    public GetOrgJESettings(ILogger<GetOrgJESettings> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")
            ?? throw new InvalidOperationException("Missing SqlConnectionString.");
    }

    [Function("GetOrgJESettings")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "organization/{orgID:int}/je-settings")]
        HttpRequestData req,
        int orgID)
    {
        _logger.LogInformation("GetOrgJESettings triggered for org {OrgID}.", orgID);

        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            const string sql = @"
                SELECT
                    o.JESummarizeBy,
                    o.JEByDate,
                    o.AccountingSoftwareID,
                    a.[Name] AS AccountingSoftwareName,
                    df.DateFormatID AS DateFormat
                FROM dbo.Organization o
                LEFT JOIN dbo.AccountingSoftware a
                    ON a.AccountingSoftwareID = o.AccountingSoftwareID
                LEFT JOIN dbo.DateFormat df
                    ON df.DateFormatID = o.DateFormatID
                WHERE o.EMAOrganizationID = @OrgID;";

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@OrgID", orgID);

            await using var reader = await cmd.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { message = $"Organization {orgID} not found." });
                return notFound;
            }

            var settings = new JeSettingsDTO
            {
                JESummarizeBy = GetString(reader, "JESummarizeBy"),
                JEByDate = GetString(reader, "JEByDate"),
                AccountingSoftwareID = GetNullableInt(reader, "AccountingSoftwareID"),
                AccountingSoftwareName = GetString(reader, "AccountingSoftwareName"),
                DateFormat = GetString(reader, "DateFormat")
            };

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(settings);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching JE settings for org {OrgID}.", orgID);

            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { message = "Error fetching JE settings." });
            return error;
        }
    }

    private static string? GetString(SqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal)?.ToString()?.Trim();
    }

    private static int? GetNullableInt(SqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : Convert.ToInt32(reader.GetValue(ordinal));
    }
}
