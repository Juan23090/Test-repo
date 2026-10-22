using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Text.Json;

namespace EMA_Api.Functions.Customization;

/// <summary>
/// PUT /api/customization/user-fund-shortlist/{orgID} — saves the signed-in user's whole fund
/// shortlist in one call. The overlay commits on Save, so the entire edited list arrives together
/// rather than a request per row.
///
/// Expected body: { "Items": [ { "EMAFundID": 1, "Selected": true, "Sequence": 1 }, ... ] }
/// Property names are matched case-insensitively, so a camelCase client (PutAsJsonAsync, which
/// defaults to JsonSerializerDefaults.Web) and a PascalCase one both work.
/// </summary>
public class PutUserFundShortList
{
    private readonly ILogger<PutUserFundShortList> _logger;
    private readonly string _connectionString;

    private static readonly JsonSerializerOptions RequestOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public PutUserFundShortList(ILogger<PutUserFundShortList> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("PutUserFundShortList")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "customization/user-fund-shortlist/{orgID:int}")] HttpRequest req,
        int orgID)
    {
        _logger.LogInformation("PutUserFundShortList triggered for org {OrgID}.", orgID);

        try
        {
            var identity = req.GetRequiredApiIdentity();

            var request = await JsonSerializer.DeserializeAsync<ShortListRequest>(
                req.Body,
                RequestOptions);

            if (request?.Items is null)
            {
                return new BadRequestObjectResult(new { message = "Body must contain an Items array." });
            }

            // Hand the proc Selected as the CHAR(1) the column stores.
            var payload = JsonSerializer.Serialize(new
            {
                Items = request.Items.Select(item => new
                {
                    item.EMAFundID,
                    Selected = item.Selected ? "Y" : "N",
                    item.Sequence
                })
            });

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(
                "EXEC dbo.sp_SaveUserFundShortList @EMAOrganizationID = @EMAOrganizationID, @UserID = @UserID, @JsonPayload = @JsonPayload",
                conn);

            cmd.Parameters.AddWithValue("@EMAOrganizationID", identity.EMAOrganizationID);
            cmd.Parameters.AddWithValue("@UserID", identity.UserID);
            cmd.Parameters.AddWithValue("@JsonPayload", payload);

            await cmd.ExecuteNonQueryAsync();

            return new OkResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving the user fund shortlist for org {OrgID}.", orgID);
            return new StatusCodeResult(500);
        }
    }

    private sealed class ShortListRequest
    {
        public List<ShortListItem>? Items { get; set; }
    }

    private sealed class ShortListItem
    {
        public int EMAFundID { get; set; }

        public bool Selected { get; set; }

        public int? Sequence { get; set; }
    }
}
