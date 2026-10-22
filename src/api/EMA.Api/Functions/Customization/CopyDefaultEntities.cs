using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using EMA.Api.Services.Security;

namespace EMA_Api.Functions.Organization;

/// <summary>
/// Copies active default entries from the configured template organization:
/// first deactivates the target org's currently-active rows, then inserts copies of the
/// active rows. Inline transactional SQL (no stored proc). No hard deletes.
/// </summary>
public class CopyDefaultEntities
{
    private readonly ILogger<CopyDefaultEntities> _logger;
    private readonly string _connectionString;
    private readonly int _sourceOrganizationID;

    public CopyDefaultEntities(ILogger<CopyDefaultEntities> logger)
    {
        _logger = logger;
        _connectionString =
            Environment.GetEnvironmentVariable(
                "SqlConnectionString")
            ?? throw new InvalidOperationException(
                "Missing SqlConnectionString.");

        if (!int.TryParse(
                Environment.GetEnvironmentVariable(
                    "DEFAULT_TEMPLATE_ORGANIZATION_ID"),
                out _sourceOrganizationID) ||
            _sourceOrganizationID <= 0)
        {
            throw new InvalidOperationException(
                "DEFAULT_TEMPLATE_ORGANIZATION_ID must be a positive integer.");
        }
    }

    [Function("CopyDefaultEntities")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "customization/{entity}/copy-defaults/{orgID:int}")] HttpRequest req,
        string entity, int orgID)
    {
        _logger.LogInformation("CopyDefaultEntities triggered for {Entity} into org {OrgID}.", entity, orgID);

        var identity =
            req.GetRequiredApiIdentity();

        if (orgID == _sourceOrganizationID)
        {
            return new OkObjectResult(new { copied = 0, message = "Target is the EMA default organization; nothing to copy." });
        }

        var sql = BuildSql(entity);
        if (sql is null)
        {
            return new BadRequestObjectResult($"Unknown entity '{entity}'.");
        }

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var tx =
                (SqlTransaction)await conn
                    .BeginTransactionAsync(
                        System.Data
                            .IsolationLevel
                            .Serializable);
            try
            {
                // Guard against a destructive wipe: if EMA has no active rows to copy, make NO
                // changes (do not deactivate the target's existing entries).
                // Most lookups store IsActive as CHAR(1) 'Y'/'N'; DonorStatus uses a real bit.
                var activeFilter = entity == "donor-statuses" ? "IsActive = 1" : "IsActive = 'Y'";
                int sourceCount;
                using (var count = new SqlCommand(
                    $"SELECT COUNT(*) FROM {sql.Value.Table} WHERE EMAOrganizationID = @Source AND {activeFilter};",
                    conn, tx))
                {
                    count.Parameters.AddWithValue(
                        "@Source",
                        _sourceOrganizationID);
                    sourceCount = Convert.ToInt32(await count.ExecuteScalarAsync());
                }

                if (sourceCount == 0)
                {
                    await tx.RollbackAsync();
                    return new OkObjectResult(new { copied = 0, message = "EMA has no active default entries for this entity; no changes were made." });
                }

                using (var deactivate = new SqlCommand(sql.Value.Inactivate, conn, tx))
                {
                    deactivate.Parameters.AddWithValue("@Target", orgID);
                    deactivate.Parameters.AddWithValue(
                        "@ActorUserID",
                        identity.UserID);
                    await deactivate.ExecuteNonQueryAsync();
                }

                int copied;
                using (var insert = new SqlCommand(sql.Value.Insert, conn, tx))
                {
                    insert.Parameters.AddWithValue("@Target", orgID);
                    insert.Parameters.AddWithValue(
                        "@Source",
                        _sourceOrganizationID);
                    insert.Parameters.AddWithValue(
                        "@ActorUserID",
                        identity.UserID);
                    copied = await insert.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();
                return new OkObjectResult(new { copied });
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error copying default {Entity} into org {OrgID}.", entity, orgID);
            return new StatusCodeResult(500);
        }
    }

    // Returns (table, inactivate, insert) SQL for the entity slug, or null if unknown.
    // Identity layout per table (confirmed via sys.identity_columns):
    //   OrganizationEvent           -> Sequence is identity (omit it); EMAEventID assigned MAX+1.
    //   OrganizationDonationSource  -> EMADonationSourceID is identity (omit); Sequence copied.
    //   OrganizationTributeType     -> no identity (assign EMATributeTypeID MAX+1); NO audit columns.
    //   OrganizationCampus/Fund/MethodOfGiving -> PK is identity (omit); Sequence copied.
    // The deactivation UPDATE stamps DateTimeUpdated/UpdatedByUserID on every table that has them
    // (all except OrganizationTributeType, which has no audit columns).
    // Cross-entity references (Campus.DefaultCampusFundID, Fund.EMACampusID) are nulled on copy.
    private static (string Table, string Inactivate, string Insert)? BuildSql(string entity) => entity switch
    {
        "events" => (
            "OrganizationEvent",
            "UPDATE OrganizationEvent SET IsActive = 'N', DateTimeUpdated = GETUTCDATE(), UpdatedByUserID = @ActorUserID WHERE EMAOrganizationID = @Target AND IsActive = 'Y';",
            @"INSERT INTO OrganizationEvent
                  (EMAEventID, EMAOrganizationID, ChMSEventID, Description, IsActive, DateTimeCreated, CreatedByUserID)
              SELECT (SELECT ISNULL(MAX(EMAEventID), 0) FROM OrganizationEvent WITH (UPDLOCK, HOLDLOCK))
                         + ROW_NUMBER() OVER (ORDER BY s.EMAEventID),
                     @Target, s.ChMSEventID, s.Description, 'Y', GETUTCDATE(), @ActorUserID
              FROM OrganizationEvent s
              WHERE s.EMAOrganizationID = @Source AND s.IsActive = 'Y';"
        ),

        "donation-sources" => (
            "OrganizationDonationSource",
            "UPDATE OrganizationDonationSource SET IsActive = 'N', DateTimeUpdated = GETUTCDATE(), UpdatedByUserID = @ActorUserID WHERE EMAOrganizationID = @Target AND IsActive = 'Y';",
            @"INSERT INTO OrganizationDonationSource
                  (EMAOrganizationID, ChMSDonationSource, Sequence, Description, IsActive, DateTimeCreated, CreatedByUserID)
              SELECT @Target, s.ChMSDonationSource, s.Sequence, s.Description, 'Y', GETUTCDATE(), @ActorUserID
              FROM OrganizationDonationSource s
              WHERE s.EMAOrganizationID = @Source AND s.IsActive = 'Y';"
        ),

        "tribute-types" => (
            "OrganizationTributeType",
            "UPDATE OrganizationTributeType SET IsActive = 'N' WHERE EMAOrganizationID = @Target AND IsActive = 'Y';",
            @"INSERT INTO OrganizationTributeType
                  (EMATributeTypeID, EMAOrganizationID, ChMSTributeTypeID, Sequence, Description, IsActive)
              SELECT (SELECT ISNULL(MAX(EMATributeTypeID), 0) FROM OrganizationTributeType WITH (UPDLOCK, HOLDLOCK))
                         + ROW_NUMBER() OVER (ORDER BY s.EMATributeTypeID),
                     @Target, s.ChMSTributeTypeID, s.Sequence, s.Description, 'Y'
              FROM OrganizationTributeType s
              WHERE s.EMAOrganizationID = @Source AND s.IsActive = 'Y';"
        ),

        "campuses" => (
            "OrganizationCampus",
            "UPDATE OrganizationCampus SET IsActive = 'N', DateTimeUpdated = GETUTCDATE(), UpdatedByUserID = @ActorUserID WHERE EMAOrganizationID = @Target AND IsActive = 'Y';",
            @"INSERT INTO OrganizationCampus
                  (EMAOrganizationID, ChMSCampusID, Sequence, Name, DefaultCampusFundID, IsActive, DateTimeCreated, CreatedByUserID)
              SELECT @Target, s.ChMSCampusID, s.Sequence, s.Name, NULL, 'Y', GETUTCDATE(), @ActorUserID
              FROM OrganizationCampus s
              WHERE s.EMAOrganizationID = @Source AND s.IsActive = 'Y';"
        ),

        "funds" => (
            "OrganizationFund",
            "UPDATE OrganizationFund SET IsActive = 'N', DateTimeUpdated = GETUTCDATE(), UpdatedByUserID = @ActorUserID WHERE EMAOrganizationID = @Target AND IsActive = 'Y';",
            @"INSERT INTO OrganizationFund
                  (EMAOrganizationID, ChMSFundID, Sequence, Name, GLAccountNumber, TaxDeductible, EMACampusID, IsActive, DateTimeCreated, CreatedByUserID)
              SELECT @Target, s.ChMSFundID, s.Sequence, s.Name, s.GLAccountNumber, s.TaxDeductible, NULL, 'Y', GETUTCDATE(), @ActorUserID
              FROM OrganizationFund s
              WHERE s.EMAOrganizationID = @Source AND s.IsActive = 'Y';"
        ),

        "methods" => (
            "OrganizationMethodOfGiving",
            "UPDATE OrganizationMethodOfGiving SET IsActive = 'N', DateTimeUpdated = GETUTCDATE(), UpdatedByUserID = @ActorUserID WHERE EMAOrganizationID = @Target AND IsActive = 'Y';",
            @"INSERT INTO OrganizationMethodOfGiving
                  (EMAOrganizationID, ChMSMethodOfGivingID, Sequence, Description, GLAccountNumber, InvoiceGroup,
                   CRAReceiptable, AcknowledgeToDonor, AcknowledgeToOrganization, AcknowledgeToEMASupport,
                   IsActive, DateTimeCreated, CreatedByUserID)
              SELECT @Target, s.ChMSMethodOfGivingID, s.Sequence, s.Description, s.GLAccountNumber, s.InvoiceGroup,
                     s.CRAReceiptable, s.AcknowledgeToDonor, s.AcknowledgeToOrganization, s.AcknowledgeToEMASupport,
                     'Y', GETUTCDATE(), @ActorUserID
              FROM OrganizationMethodOfGiving s
              WHERE s.EMAOrganizationID = @Source AND s.IsActive = 'Y';"
        ),

        "donor-statuses" => (
            "DonorStatus",
            "UPDATE DonorStatus SET IsActive = 0 WHERE EMAOrganizationId = @Target AND IsActive = 1;",
            @"INSERT INTO DonorStatus
                  (EMAOrganizationId, DonorStatusID, Sequence, Description, IsActive)
              SELECT @Target,
                     (SELECT ISNULL(MAX(DonorStatusID), 0) FROM DonorStatus)
                         + ROW_NUMBER() OVER (ORDER BY s.DonorStatusID),
                     s.Sequence, s.Description, 1
              FROM DonorStatus s
              WHERE s.EMAOrganizationId = @Source AND s.IsActive = 1;"
        ),

        "interaction-types" => (
            "OrganizationInteractionType",
            "UPDATE OrganizationInteractionType SET IsActive = 'N', DateTimeUpdated = GETUTCDATE(), UpdatedByUserID = @ActorUserID WHERE EMAOrganizationID = @Target AND IsActive = 'Y';",
            @"INSERT INTO OrganizationInteractionType
                  (EMAOrganizationID, Sequence, Description, IsActive, DateTimeCreated, CreatedByUserID)
              SELECT @Target, s.Sequence, s.Description, 'Y', GETUTCDATE(), @ActorUserID
              FROM OrganizationInteractionType s
              WHERE s.EMAOrganizationID = @Source AND s.IsActive = 'Y';"
        ),

        _ => null
    };
}
