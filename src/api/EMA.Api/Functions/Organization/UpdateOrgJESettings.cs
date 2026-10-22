using System.Data;
using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using EMA.Api.DTOs;

namespace EMA_Api.Functions.Organization;

public class UpdateOrgJESettings
{
    private readonly ILogger<UpdateOrgJESettings> _logger;
    private readonly string _connectionString;

    public UpdateOrgJESettings(ILogger<UpdateOrgJESettings> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")
            ?? throw new InvalidOperationException("Missing SqlConnectionString.");
    }

    [Function("UpdateOrgJESettings")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "organization/{orgID:int}/je-settings")]
        HttpRequestData req,
        int orgID)
    {
        _logger.LogInformation("UpdateOrgJESettings triggered for org {OrgID}.", orgID);

        try
        {
            var body = await req.ReadFromJsonAsync<JeSettingsDTO>();

            if (body is null)
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { message = "Body is required." });
                return bad;
            }

            // Normalize to a single 'A'/'D' and 'Y'/'N' char; default to Description / single journal.
            var summarizeBy = NormalizeChar(body.JESummarizeBy, allowed: "AD", fallback: 'D');
            var jeByDate = NormalizeChar(body.JEByDate, allowed: "YN", fallback: 'N');

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = new SqlCommand(
                "EXEC sp_UpdateOrgJESettings @EMAOrganizationID, @JESummarizeBy, @JEByDate, @UpdatedByUserID",
                conn);
            cmd.Parameters.Add("@EMAOrganizationID", SqlDbType.Int).Value = orgID;
            cmd.Parameters.Add("@JESummarizeBy", SqlDbType.Char, 1).Value = summarizeBy.ToString();
            cmd.Parameters.Add("@JEByDate", SqlDbType.Char, 1).Value = jeByDate.ToString();
            cmd.Parameters.Add("@UpdatedByUserID", SqlDbType.Int).Value = req.GetRequiredApiIdentity().UserID;

            var rows = await cmd.ExecuteNonQueryAsync();

            if (rows == 0)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { message = "Organization not found." });
                return notFound;
            }

            return req.CreateResponse(HttpStatusCode.NoContent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating JE settings for org {OrgID}.", orgID);

            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { message = "Error updating JE settings." });
            return error;
        }
    }

    private static char NormalizeChar(string? value, string allowed, char fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var c = char.ToUpperInvariant(value.Trim()[0]);
        return allowed.IndexOf(c) >= 0 ? c : fallback;
    }
}

