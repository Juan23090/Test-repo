using System.Net;
using EMA.Api.DTOs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA_Api.Functions.Customization;

public class GetDateFormats
{
    private readonly ILogger<GetDateFormats> _logger;
    private readonly string _connectionString;

    public GetDateFormats(ILogger<GetDateFormats> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")
            ?? throw new InvalidOperationException("Missing SqlConnectionString.");
    }

    [Function("GetDateFormats")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "date-formats")]
        HttpRequestData req)
    {
        _logger.LogInformation("GetDateFormats triggered.");

        try
        {
            var results = new List<DateFormatDTO>();

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            const string sql = @"
                SELECT DateFormatID
                FROM dbo.DateFormat
                ORDER BY DateFormatID;";

            await using var cmd = new SqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var dateFormatID = reader.GetValue(reader.GetOrdinal("DateFormatID"))?.ToString();

                if (!string.IsNullOrWhiteSpace(dateFormatID))
                {
                    results.Add(new DateFormatDTO
                    {
                        DateFormatID = dateFormatID
                    });
                }
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(results);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching date formats.");

            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { message = "Error fetching date formats." });
            return error;
        }
    }
}