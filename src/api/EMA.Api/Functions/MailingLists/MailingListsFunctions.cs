using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Functions.MailingLists;

public sealed class MailingListsFunctions
{
    private readonly ILogger<MailingListsFunctions> _logger;
    private readonly string _connectionString;

    public MailingListsFunctions(ILogger<MailingListsFunctions> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")
            ?? throw new InvalidOperationException("Missing SqlConnectionString.");
    }

    [Function("GetMailingLists")]
    public async Task<IActionResult> GetMailingLists(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "mailing-lists/{orgID:int}")] HttpRequest request,
        int orgID)
    {
        try
        {
            bool showInactive = request.Query.ContainsKey("showInactive") 
                && (string.Equals(request.Query["showInactive"], "true", StringComparison.OrdinalIgnoreCase) 
                    || string.Equals(request.Query["showInactive"], "1", StringComparison.OrdinalIgnoreCase));

            var result = new List<Dictionary<string, object?>>();
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = """
                SELECT 
                    ml.MailingListID,
                    ml.Name,
                    ml.Description,
                    ml.IsActive,
                    ISNULL(
                        (SELECT TOP 1 CONVERT(VARCHAR, mli.DateTimeCreated, 111)
                         FROM dbo.OrganizationMailingListInstance mli
                         WHERE mli.MailingListID = ml.MailingListID
                         ORDER BY mli.DateTimeCreated DESC), 
                        'N/A'
                    ) AS LastInstance,
                    ISNULL(
                        (SELECT TOP 1 mli.DonorsSelected - mli.DonorsOptedOut + mli.DonorsOptedIn
                         FROM dbo.OrganizationMailingListInstance mli
                         WHERE mli.MailingListID = ml.MailingListID
                         ORDER BY mli.DateTimeCreated DESC), 
                        ml.NumberDonorsInMailingList
                    ) AS SendListCount
                FROM dbo.OrganizationMailingList ml
                WHERE ml.EMAOrganizationID = @OrgID AND ml.IsActive = @IsActive
                ORDER BY ml.Name;
            """;

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@OrgID", orgID);
            command.Parameters.AddWithValue("@IsActive", showInactive ? 0 : 1);

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
                result.Add(row);
            }

            return new OkObjectResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting mailing lists for org {OrgID}", orgID);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    [Function("CreateMailingList")]
    public async Task<IActionResult> CreateMailingList(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "mailing-lists/{orgID:int}")] HttpRequest request,
        int orgID)
    {
        try
        {
            int userID = request.GetRequiredApiIdentity().UserID;

            using var reader = new StreamReader(request.Body);
            string body = await reader.ReadToEndAsync();
            var json = System.Text.Json.JsonDocument.Parse(body);
            var root = json.RootElement;

            string name = root.GetProperty("name").GetString() ?? "New Mailing List";
            string? description = root.TryGetProperty("description", out var dEl) ? dEl.GetString() : null;
            DateTime? donation1StartDate = root.TryGetProperty("donation1StartDate", out var d1sEl) && d1sEl.ValueKind != System.Text.Json.JsonValueKind.Null && DateTime.TryParse(d1sEl.GetString(), out var d1s) ? d1s : null;
            DateTime? donation1EndDate = root.TryGetProperty("donation1EndDate", out var d1eEl) && d1eEl.ValueKind != System.Text.Json.JsonValueKind.Null && DateTime.TryParse(d1eEl.GetString(), out var d1e) ? d1e : null;
            decimal? donation1MoneyFrom = root.TryGetProperty("donation1MoneyFrom", out var d1mfEl) && d1mfEl.ValueKind != System.Text.Json.JsonValueKind.Null ? d1mfEl.GetDecimal() : null;
            decimal? donation1MoneyTo = root.TryGetProperty("donation1MoneyTo", out var d1mtEl) && d1mtEl.ValueKind != System.Text.Json.JsonValueKind.Null ? d1mtEl.GetDecimal() : null;
            string andOr = root.TryGetProperty("andOr", out var aoEl) && !string.IsNullOrEmpty(aoEl.GetString()) ? aoEl.GetString()! : "And";
            DateTime? donation2StartDate = root.TryGetProperty("donation2StartDate", out var d2sEl) && d2sEl.ValueKind != System.Text.Json.JsonValueKind.Null && DateTime.TryParse(d2sEl.GetString(), out var d2s) ? d2s : null;
            DateTime? donation2EndDate = root.TryGetProperty("donation2EndDate", out var d2eEl) && d2eEl.ValueKind != System.Text.Json.JsonValueKind.Null && DateTime.TryParse(d2eEl.GetString(), out var d2e) ? d2e : null;
            decimal? donation2MoneyFrom = root.TryGetProperty("donation2MoneyFrom", out var d2mfEl) && d2mfEl.ValueKind != System.Text.Json.JsonValueKind.Null ? d2mfEl.GetDecimal() : null;
            decimal? donation2MoneyTo = root.TryGetProperty("donation2MoneyTo", out var d2mtEl) && d2mtEl.ValueKind != System.Text.Json.JsonValueKind.Null ? d2mtEl.GetDecimal() : null;
            int outputFormatID = root.TryGetProperty("outputFormatID", out var ofEl) && ofEl.ValueKind != System.Text.Json.JsonValueKind.Null ? ofEl.GetInt32() : 1;
            int sendMethodID = root.TryGetProperty("sendMethodID", out var smEl) && smEl.ValueKind != System.Text.Json.JsonValueKind.Null ? smEl.GetInt32() : 1;
            bool isActive = root.TryGetProperty("isActive", out var iaEl) ? iaEl.GetBoolean() : true;

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = """
                INSERT INTO dbo.OrganizationMailingList (
                    EMAOrganizationID, Name, Description,
                    Donation1StartDate, Donation1EndDate, Donation1MoneyFrom, Donation1MoneyTo,
                    AndOr,
                    Donation2StartDate, Donation2EndDate, Donation2MoneyFrom, Donation2MoneyTo,
                    OutputFormatID, SendMethodID, IsActive, DateTimeCreated, CreatedByUserID
                )
                OUTPUT INSERTED.MailingListID
                VALUES (
                    @OrgID, @Name, @Description,
                    @Donation1StartDate, @Donation1EndDate, @Donation1MoneyFrom, @Donation1MoneyTo,
                    @AndOr,
                    @Donation2StartDate, @Donation2EndDate, @Donation2MoneyFrom, @Donation2MoneyTo,
                    @OutputFormatID, @SendMethodID, @IsActive, GETDATE(), @UserID
                );
            """;

            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@OrgID", orgID);
            cmd.Parameters.AddWithValue("@Name", name);
            cmd.Parameters.AddWithValue("@Description", (object?)description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Donation1StartDate", (object?)donation1StartDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Donation1EndDate", (object?)donation1EndDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Donation1MoneyFrom", (object?)donation1MoneyFrom ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Donation1MoneyTo", (object?)donation1MoneyTo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@AndOr", andOr);
            cmd.Parameters.AddWithValue("@Donation2StartDate", (object?)donation2StartDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Donation2EndDate", (object?)donation2EndDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Donation2MoneyFrom", (object?)donation2MoneyFrom ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Donation2MoneyTo", (object?)donation2MoneyTo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@OutputFormatID", outputFormatID);
            cmd.Parameters.AddWithValue("@SendMethodID", sendMethodID);
            cmd.Parameters.AddWithValue("@IsActive", isActive);
            cmd.Parameters.AddWithValue("@UserID", userID);

            int newID = (int)(await cmd.ExecuteScalarAsync() ?? 0);
            return new OkObjectResult(new { MailingListID = newID, Message = "Mailing list created successfully." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating mailing list for org {OrgID}", orgID);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    [Function("UpdateMailingList")]
    public async Task<IActionResult> UpdateMailingList(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "mailing-lists/{orgID:int}/{listID:int}")] HttpRequest request,
        int orgID,
        int listID)
    {
        try
        {
            int userID = request.GetRequiredApiIdentity().UserID;

            using var reader = new StreamReader(request.Body);
            string body = await reader.ReadToEndAsync();
            var json = System.Text.Json.JsonDocument.Parse(body);
            var root = json.RootElement;

            string name = root.GetProperty("name").GetString() ?? "Mailing List";
            string? description = root.TryGetProperty("description", out var dEl) ? dEl.GetString() : null;
            DateTime? donation1StartDate = root.TryGetProperty("donation1StartDate", out var d1sEl) && d1sEl.ValueKind != System.Text.Json.JsonValueKind.Null && DateTime.TryParse(d1sEl.GetString(), out var d1s) ? d1s : null;
            DateTime? donation1EndDate = root.TryGetProperty("donation1EndDate", out var d1eEl) && d1eEl.ValueKind != System.Text.Json.JsonValueKind.Null && DateTime.TryParse(d1eEl.GetString(), out var d1e) ? d1e : null;
            decimal? donation1MoneyFrom = root.TryGetProperty("donation1MoneyFrom", out var d1mfEl) && d1mfEl.ValueKind != System.Text.Json.JsonValueKind.Null ? d1mfEl.GetDecimal() : null;
            decimal? donation1MoneyTo = root.TryGetProperty("donation1MoneyTo", out var d1mtEl) && d1mtEl.ValueKind != System.Text.Json.JsonValueKind.Null ? d1mtEl.GetDecimal() : null;
            string andOr = root.TryGetProperty("andOr", out var aoEl) && !string.IsNullOrEmpty(aoEl.GetString()) ? aoEl.GetString()! : "And";
            DateTime? donation2StartDate = root.TryGetProperty("donation2StartDate", out var d2sEl) && d2sEl.ValueKind != System.Text.Json.JsonValueKind.Null && DateTime.TryParse(d2sEl.GetString(), out var d2s) ? d2s : null;
            DateTime? donation2EndDate = root.TryGetProperty("donation2EndDate", out var d2eEl) && d2eEl.ValueKind != System.Text.Json.JsonValueKind.Null && DateTime.TryParse(d2eEl.GetString(), out var d2e) ? d2e : null;
            decimal? donation2MoneyFrom = root.TryGetProperty("donation2MoneyFrom", out var d2mfEl) && d2mfEl.ValueKind != System.Text.Json.JsonValueKind.Null ? d2mfEl.GetDecimal() : null;
            decimal? donation2MoneyTo = root.TryGetProperty("donation2MoneyTo", out var d2mtEl) && d2mtEl.ValueKind != System.Text.Json.JsonValueKind.Null ? d2mtEl.GetDecimal() : null;
            int outputFormatID = root.TryGetProperty("outputFormatID", out var ofEl) && ofEl.ValueKind != System.Text.Json.JsonValueKind.Null ? ofEl.GetInt32() : 1;
            int sendMethodID = root.TryGetProperty("sendMethodID", out var smEl) && smEl.ValueKind != System.Text.Json.JsonValueKind.Null ? smEl.GetInt32() : 1;
            bool isActive = root.TryGetProperty("isActive", out var iaEl) ? iaEl.GetBoolean() : true;
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            if (!await MailingListBelongsToOrganizationAsync(connection, orgID, listID))
            {
                return new NotFoundResult();
            }

            const string sql = """
                UPDATE dbo.OrganizationMailingList
                SET Name = @Name,
                    Description = @Description,
                    Donation1StartDate = @Donation1StartDate,
                    Donation1EndDate = @Donation1EndDate,
                    Donation1MoneyFrom = @Donation1MoneyFrom,
                    Donation1MoneyTo = @Donation1MoneyTo,
                    AndOr = @AndOr,
                    Donation2StartDate = @Donation2StartDate,
                    Donation2EndDate = @Donation2EndDate,
                    Donation2MoneyFrom = @Donation2MoneyFrom,
                    Donation2MoneyTo = @Donation2MoneyTo,
                    OutputFormatID = @OutputFormatID,
                    SendMethodID = @SendMethodID,
                    IsActive = @IsActive,
                    DateTimeUpdated = GETDATE(),
                    UpdatedByUserID = @UserID
                WHERE EMAOrganizationID = @OrgID AND MailingListID = @ListID;
            """;

            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@OrgID", orgID);
            cmd.Parameters.AddWithValue("@ListID", listID);
            cmd.Parameters.AddWithValue("@Name", name);
            cmd.Parameters.AddWithValue("@Description", (object?)description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Donation1StartDate", (object?)donation1StartDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Donation1EndDate", (object?)donation1EndDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Donation1MoneyFrom", (object?)donation1MoneyFrom ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Donation1MoneyTo", (object?)donation1MoneyTo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@AndOr", andOr);
            cmd.Parameters.AddWithValue("@Donation2StartDate", (object?)donation2StartDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Donation2EndDate", (object?)donation2EndDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Donation2MoneyFrom", (object?)donation2MoneyFrom ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Donation2MoneyTo", (object?)donation2MoneyTo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@OutputFormatID", outputFormatID);
            cmd.Parameters.AddWithValue("@SendMethodID", sendMethodID);
            cmd.Parameters.AddWithValue("@IsActive", isActive);
            cmd.Parameters.AddWithValue("@UserID", userID);

            await cmd.ExecuteNonQueryAsync();
            return new OkObjectResult(new { Message = "Mailing list updated successfully." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating mailing list {ListID} for org {OrgID}", listID, orgID);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    [Function("GetMailingListDetails")]
    public async Task<IActionResult> GetMailingListDetails(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "mailing-lists/{orgID:int}/{listID:int}")] HttpRequest request,
        int orgID,
        int listID)
    {
        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sqlList = """
                SELECT 
                    ml.*,
                    ofmt.Description AS OutputFormatName,
                    sm.Description AS SendMethodName,
                    uCreate.UserName AS CreatedBy,
                    uUpdate.UserName AS UpdatedBy
                FROM dbo.OrganizationMailingList ml
                LEFT JOIN dbo.MailingListOutputFormat ofmt ON ml.OutputFormatID = ofmt.OutputFormatID
                LEFT JOIN dbo.MailingListSendMethod sm ON ml.SendMethodID = sm.SendMethodID
                LEFT JOIN dbo.[User] uCreate ON ml.CreatedByUserID = uCreate.UserID
                LEFT JOIN dbo.[User] uUpdate ON ml.UpdatedByUserID = uUpdate.UserID
                WHERE ml.EMAOrganizationID = @OrgID AND ml.MailingListID = @ListID;
            """;

            Dictionary<string, object?>? listDetails = null;
            await using (var cmd = new SqlCommand(sqlList, connection))
            {
                cmd.Parameters.AddWithValue("@OrgID", orgID);
                cmd.Parameters.AddWithValue("@ListID", listID);
                await using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    listDetails = new Dictionary<string, object?>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        listDetails[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    }
                }
            }

            if (listDetails == null)
            {
                return new NotFoundResult();
            }

            const string sqlInstances = """
                SELECT 
                    MailingListInstanceID,
                    DateTimeCreated,
                    DonorsSelected AS Selected,
                    DonorsOptedIn AS OptedIn,
                    DonorsOptedOut AS OptedOut,
                    (DonorsSelected - DonorsOptedOut + DonorsOptedIn) AS SendList
                FROM dbo.OrganizationMailingListInstance
                WHERE OrganizationID = @OrgID AND MailingListID = @ListID
                ORDER BY DateTimeCreated DESC;
            """;

            var instances = new List<Dictionary<string, object?>>();
            await using (var cmdInstance = new SqlCommand(sqlInstances, connection))
            {
                cmdInstance.Parameters.AddWithValue("@OrgID", orgID);
                cmdInstance.Parameters.AddWithValue("@ListID", listID);
                await using var reader = await cmdInstance.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var row = new Dictionary<string, object?>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    }
                    instances.Add(row);
                }
            }

            listDetails["Instances"] = instances;
            return new OkObjectResult(listDetails);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting mailing list details for list {ListID}", listID);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    [Function("PrepareMailingList")]
    public async Task<IActionResult> PrepareMailingList(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "mailing-lists/{orgID:int}/{listID:int}/prepare")] HttpRequest request,
        int orgID,
        int listID)
    {
        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            if (!await MailingListBelongsToOrganizationAsync(connection, orgID, listID))
            {
                return new NotFoundResult();
            }

            await using var cmd = new SqlCommand("dbo.sp_PrepareMailingListInstance", connection);
            cmd.CommandType = System.Data.CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@EMAOrganizationID", orgID);
            cmd.Parameters.AddWithValue("@MailingListID", listID);
            
            var outputParam = new SqlParameter("@NewInstanceID", System.Data.SqlDbType.Int)
            {
                Direction = System.Data.ParameterDirection.Output
            };
            cmd.Parameters.Add(outputParam);

            await cmd.ExecuteNonQueryAsync();

            int newInstanceID = (int)(outputParam.Value ?? 0);
            return new OkObjectResult(new { InstanceID = newInstanceID, Message = "Mailing list prepared successfully." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error preparing mailing list instance for list {ListID}", listID);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    [Function("ExportMailingListCSV")]
    public async Task<IActionResult> ExportMailingListCSV(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "mailing-lists/{orgID:int}/instance/{instanceID:int}/export")] HttpRequest request,
        int orgID,
        int instanceID)
    {
        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            const string instanceOwnershipSql = """
                SELECT COUNT(1)
                FROM dbo.OrganizationMailingListInstance mli
                INNER JOIN dbo.OrganizationMailingList ml
                    ON ml.MailingListID = mli.MailingListID
                   AND ml.EMAOrganizationID = @OrgID
                WHERE mli.MailingListInstanceID = @InstanceID
                  AND mli.OrganizationID = @OrgID;
            """;

            await using (var ownershipCmd = new SqlCommand(instanceOwnershipSql, connection))
            {
                ownershipCmd.Parameters.AddWithValue("@OrgID", orgID);
                ownershipCmd.Parameters.AddWithValue("@InstanceID", instanceID);
                if (Convert.ToInt32(await ownershipCmd.ExecuteScalarAsync()) == 0)
                {
                    return new NotFoundResult();
                }
            }

            const string sql = """
                IF EXISTS (
                    SELECT 1
                    FROM dbo.OrganizationMailingListInstanceDonor mlid
                    INNER JOIN dbo.OrganizationMailingListInstance mli
                        ON mli.MailingListInstanceID = mlid.MailingListInstanceID
                    INNER JOIN dbo.OrganizationMailingList ml
                        ON ml.MailingListID = mli.MailingListID
                       AND ml.EMAOrganizationID = @OrgID
                    WHERE mlid.MailingListInstanceID = @InstanceID
                      AND mlid.EMAOrganizationID = @OrgID
                      AND mli.OrganizationID = @OrgID
                )
                BEGIN
                    SELECT 
                        mlid.OfficialDonorName,
                        mlid.EmailAddress,
                        mlid.Address,
                        mlid.City,
                        mlid.ProvinceStateCode,
                        mlid.PostalCode,
                        mlid.Country
                    FROM dbo.OrganizationMailingListInstanceDonor mlid
                    INNER JOIN dbo.OrganizationMailingListInstance mli
                        ON mli.MailingListInstanceID = mlid.MailingListInstanceID
                    INNER JOIN dbo.OrganizationMailingList ml
                        ON ml.MailingListID = mli.MailingListID
                       AND ml.EMAOrganizationID = @OrgID
                    WHERE mlid.MailingListInstanceID = @InstanceID
                      AND mlid.EMAOrganizationID = @OrgID
                      AND mli.OrganizationID = @OrgID;
                END
                ELSE
                BEGIN
                    -- Fallback: Fetch donors matching the instance organization and mailing list criteria
                    SELECT 
                        d.OfficialDonorName,
                        d.EmailAddress,
                        d.Address,
                        d.City,
                        d.ProvinceStateCode,
                        d.PostalCode,
                        d.Country
                    FROM dbo.OrganizationMailingListInstance mli
                    JOIN dbo.OrganizationMailingList ml
                      ON mli.MailingListID = ml.MailingListID
                     AND ml.EMAOrganizationID = @OrgID
                    JOIN dbo.Donor d ON d.EMAOrganizationID = mli.OrganizationID
                    WHERE mli.MailingListInstanceID = @InstanceID
                      AND mli.OrganizationID = @OrgID
                      AND EXISTS (
                          SELECT 1 FROM dbo.Donation don
                          WHERE don.EMAOrganizationID = d.EMAOrganizationID AND don.EMADonorID = d.EMADonorID
                            AND (ml.Donation1StartDate IS NULL OR don.DateTimeReceived >= ml.Donation1StartDate)
                            AND (ml.Donation1EndDate IS NULL OR don.DateTimeReceived <= ml.Donation1EndDate)
                            AND (ml.Donation1MoneyFrom IS NULL OR don.DonationAmount >= ml.Donation1MoneyFrom)
                            AND (ml.Donation1MoneyTo IS NULL OR don.DonationAmount <= ml.Donation1MoneyTo)
                      )
                      AND (
                          (ml.SendMethodID = 1 AND d.EmailAddress IS NOT NULL AND LTRIM(RTRIM(d.EmailAddress)) <> '')
                          OR
                          (ml.SendMethodID = 2 AND d.Address IS NOT NULL AND LTRIM(RTRIM(d.Address)) <> '')
                          OR
                          (ml.SendMethodID IS NULL)
                      );
                END
            """;

            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@OrgID", orgID);
            cmd.Parameters.AddWithValue("@InstanceID", instanceID);

            var sb = new StringBuilder();
            sb.AppendLine("Name,Email,Address,City,Province/State,Postal Code,Country");

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string name = EscapeCsvField(reader["OfficialDonorName"]?.ToString());
                string email = EscapeCsvField(reader["EmailAddress"]?.ToString());
                string address = EscapeCsvField(reader["Address"]?.ToString());
                string city = EscapeCsvField(reader["City"]?.ToString());
                string prov = EscapeCsvField(reader["ProvinceStateCode"]?.ToString());
                string postal = EscapeCsvField(reader["PostalCode"]?.ToString());
                string country = EscapeCsvField(reader["Country"]?.ToString());

                sb.AppendLine($"{name},{email},{address},{city},{prov},{postal},{country}");
            }

            byte[] fileBytes = Encoding.UTF8.GetBytes(sb.ToString());
            return new FileContentResult(fileBytes, "text/csv")
            {
                FileDownloadName = $"MailingList_Instance_{instanceID}.csv"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting CSV for instance {InstanceID} in org {OrgID}", instanceID, orgID);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    [Function("GetReviewDonors")]
    public async Task<IActionResult> GetReviewDonors(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "mailing-lists/{orgID:int}/{listID:int}/review")] HttpRequest request,
        int orgID,
        int listID)
    {
        try
        {
            string? search = request.Query["search"].FirstOrDefault();

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            if (!await MailingListBelongsToOrganizationAsync(connection, orgID, listID))
            {
                return new NotFoundResult();
            }

            const string sql = """
                WITH LatestDonations AS (
                    SELECT 
                        don.EMADonorID,
                        MAX(don.DateTimeReceived) AS MaxDate
                    FROM dbo.Donation don
                    WHERE don.EMAOrganizationID = @OrgID
                    GROUP BY don.EMADonorID
                ),
                CriteriaMatches AS (
                    -- When ALL Donation 1 criteria are NULL, match all org donors (no donation required).
                    -- When any criteria is set, require a donation matching those filters.
                    SELECT DISTINCT d.EMADonorID
                    FROM dbo.Donor d
                    JOIN dbo.OrganizationMailingList ml
                      ON ml.MailingListID = @ListID
                     AND ml.EMAOrganizationID = @OrgID
                    LEFT JOIN dbo.Donation don
                      ON don.EMAOrganizationID = d.EMAOrganizationID
                     AND don.EMADonorID = d.EMADonorID
                     AND (ml.Donation1StartDate IS NULL OR don.DateTimeReceived >= ml.Donation1StartDate)
                     AND (ml.Donation1EndDate IS NULL OR don.DateTimeReceived <= ml.Donation1EndDate)
                     AND (ml.Donation1MoneyFrom IS NULL OR don.DonationAmount >= ml.Donation1MoneyFrom)
                     AND (ml.Donation1MoneyTo IS NULL OR don.DonationAmount <= ml.Donation1MoneyTo)
                    WHERE d.EMAOrganizationID = @OrgID
                      AND (
                          -- All criteria NULL: any donor qualifies regardless of donations
                          (ml.Donation1StartDate IS NULL AND ml.Donation1EndDate IS NULL AND ml.Donation1MoneyFrom IS NULL AND ml.Donation1MoneyTo IS NULL)
                          OR
                          -- Some criteria set: must have a matching donation
                          don.EMADonorID IS NOT NULL
                      )
                )
                SELECT 
                    d.EMADonorID,
                    d.OfficialDonorName,
                    CONVERT(VARCHAR, ld.MaxDate, 111) AS LastDonationDate,
                    lastdon.DonationAmount AS LastDonationAmount,
                    CAST(CASE WHEN cm.EMADonorID IS NOT NULL THEN 1 ELSE 0 END AS BIT) AS Selected,
                    ISNULL(mld.OptedIn, 0) AS OptedIn,
                    ISNULL(mld.OptedOut, 0) AS OptedOut,
                    CAST(CASE 
                        WHEN ISNULL(mld.OptedOut, 0) = 1 THEN 0
                        WHEN ISNULL(mld.OptedIn, 0) = 1 THEN 1
                        WHEN cm.EMADonorID IS NOT NULL THEN 1
                        ELSE 0
                    END AS BIT) AS MailingList
                FROM dbo.Donor d
                LEFT JOIN LatestDonations ld ON ld.EMADonorID = d.EMADonorID
                LEFT JOIN dbo.Donation lastdon ON lastdon.EMAOrganizationID = d.EMAOrganizationID AND lastdon.EMADonorID = d.EMADonorID AND lastdon.DateTimeReceived = ld.MaxDate
                LEFT JOIN CriteriaMatches cm ON cm.EMADonorID = d.EMADonorID
                LEFT JOIN dbo.OrganizationMailingListDonor mld 
                    ON mld.EMAOrganizationID = d.EMAOrganizationID 
                   AND mld.MailingListID = @ListID 
                   AND mld.EMADonorID = d.EMADonorID
                WHERE d.EMAOrganizationID = @OrgID
                  AND d.OfficialDonorName IS NOT NULL 
                  AND LTRIM(RTRIM(d.OfficialDonorName)) <> ''
                  AND (@Search IS NULL OR @Search = '' OR d.OfficialDonorName LIKE '%' + @Search + '%')
                ORDER BY d.OfficialDonorName;
            """;

            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@OrgID", orgID);
            cmd.Parameters.AddWithValue("@ListID", listID);
            cmd.Parameters.AddWithValue("@Search", (object?)search ?? DBNull.Value);

            var donors = new List<Dictionary<string, object?>>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
                donors.Add(row);
            }

            return new OkObjectResult(donors);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting donors for review list {ListID}", listID);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    [Function("ToggleDonorOptStatus")]
    public async Task<IActionResult> ToggleDonorOptStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "mailing-lists/{orgID:int}/{listID:int}/donors/{donorID:int}/toggle-opt")] HttpRequest request,
        int orgID,
        int listID,
        int donorID)
    {
        try
        {
            using var reader = new StreamReader(request.Body);
            string body = await reader.ReadToEndAsync();
            var json = System.Text.Json.JsonDocument.Parse(body);
            
            bool? optedIn = json.RootElement.TryGetProperty("optedIn", out var optInElem) && (optInElem.ValueKind == System.Text.Json.JsonValueKind.True || optInElem.ValueKind == System.Text.Json.JsonValueKind.False) ? optInElem.GetBoolean() : null;
            bool? optedOut = json.RootElement.TryGetProperty("optedOut", out var optOutElem) && (optOutElem.ValueKind == System.Text.Json.JsonValueKind.True || optOutElem.ValueKind == System.Text.Json.JsonValueKind.False) ? optOutElem.GetBoolean() : null;

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            if (!await MailingListBelongsToOrganizationAsync(connection, orgID, listID) ||
                !await DonorBelongsToOrganizationAsync(connection, orgID, donorID))
            {
                return new NotFoundResult();
            }

            const string sql = """
                MERGE dbo.OrganizationMailingListDonor AS target
                USING (SELECT @OrgID AS EMAOrganizationID, @ListID AS MailingListID, @DonorID AS EMADonorID) AS source
                ON target.EMAOrganizationID = source.EMAOrganizationID 
               AND target.MailingListID = source.MailingListID 
               AND target.EMADonorID = source.EMADonorID
                WHEN MATCHED THEN
                    UPDATE SET 
                        OptedIn = ISNULL(@OptedIn, target.OptedIn),
                        OptedOut = ISNULL(@OptedOut, target.OptedOut)
                WHEN NOT MATCHED THEN
                    INSERT (EMAOrganizationID, MailingListID, EMADonorID, OptedIn, OptedOut, DateTimeCreated)
                    VALUES (source.EMAOrganizationID, source.MailingListID, source.EMADonorID, ISNULL(@OptedIn, 0), ISNULL(@OptedOut, 0), GETDATE());
            """;

            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@OrgID", orgID);
            cmd.Parameters.AddWithValue("@ListID", listID);
            cmd.Parameters.AddWithValue("@DonorID", donorID);
            cmd.Parameters.AddWithValue("@OptedIn", (object?)optedIn ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@OptedOut", (object?)optedOut ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync();

            return new OkObjectResult(new { Message = "Donor opt status updated successfully." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error toggling donor opt status for donor {DonorID} on list {ListID}", donorID, listID);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    private static string EscapeCsvField(string? field)
    {
        if (string.IsNullOrEmpty(field)) return "";
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }
        return field;
    }

    private static async Task<bool> MailingListBelongsToOrganizationAsync(
        SqlConnection connection,
        int orgID,
        int listID)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM dbo.OrganizationMailingList
            WHERE EMAOrganizationID = @OrgID
              AND MailingListID = @ListID;
        """;

        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@OrgID", orgID);
        cmd.Parameters.AddWithValue("@ListID", listID);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
    }

    private static async Task<bool> DonorBelongsToOrganizationAsync(
        SqlConnection connection,
        int orgID,
        int donorID)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM dbo.Donor
            WHERE EMAOrganizationID = @OrgID
              AND EMADonorID = @DonorID;
        """;

        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@OrgID", orgID);
        cmd.Parameters.AddWithValue("@DonorID", donorID);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
    }
}
