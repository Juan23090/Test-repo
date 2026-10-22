using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Text.Json;

namespace EMA_Api.Functions.Customization;

// Saves the landing-page graph choice for either the org ops landing page
// (KPIGraphOrganizationLandingPage) or the donor landing page (KPIGraphDonorLandingPage).
// The two are independent settings with independent defaults, so the caller says which one
// it's saving via "target" ("org" | "donor").
// Codes: 'F' = Donations by Fund, 'M' = Donations by Method, 'V' = Value (donations by month).
public class PostLandingGraph
{
    private static readonly HashSet<string> AllowedGraphs = new(StringComparer.OrdinalIgnoreCase) { "F", "M", "V" };
    private static readonly HashSet<string> AllowedTargets = new(StringComparer.OrdinalIgnoreCase) { "org", "donor" };

    private readonly ILogger<PostLandingGraph> _logger;
    private readonly string _connectionString;

    public PostLandingGraph(ILogger<PostLandingGraph> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")!;
    }

    [Function("PostLandingGraph")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "customization/landing-graph/{orgID:int}")] HttpRequest req,
        int orgID)
    {
        _logger.LogInformation("PostLandingGraph triggered for org {OrgID}.", orgID);
        try
        {
            using var doc = await JsonDocument.ParseAsync(req.Body);
            var graph = doc.RootElement.TryGetProperty("graph", out var g) ? g.GetString() : null;
            var target = doc.RootElement.TryGetProperty("target", out var t) ? t.GetString() : null;

            if (string.IsNullOrWhiteSpace(graph) || !AllowedGraphs.Contains(graph))
                return new BadRequestObjectResult(new { message = "graph must be one of 'F', 'M', or 'V'." });

            if (string.IsNullOrWhiteSpace(target) || !AllowedTargets.Contains(target))
                return new BadRequestObjectResult(new { message = "target must be 'org' or 'donor'." });

            graph = graph.ToUpperInvariant();
            target = target.ToLowerInvariant();

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = new SqlCommand(
                target == "org"
                    ? @"UPDATE Organization
                           SET KPIGraphOrganizationLandingPage = @Graph
                         WHERE EMAOrganizationID = @OrgID;"
                    : @"UPDATE Organization
                           SET KPIGraphDonorLandingPage = @Graph
                         WHERE EMAOrganizationID = @OrgID;",
                conn);
            cmd.Parameters.AddWithValue("@Graph", graph);
            cmd.Parameters.AddWithValue("@OrgID", orgID);

            var rows = await cmd.ExecuteNonQueryAsync();
            if (rows == 0)
                return new NotFoundResult();

            return new OkResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving landing graph for org {OrgID}.", orgID);
            return new StatusCodeResult(500);
        }
    }
}
