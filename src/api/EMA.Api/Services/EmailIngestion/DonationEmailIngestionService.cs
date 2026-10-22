using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace EMA.Api.Services;

public sealed class DonationEmailIngestionService : IDonationEmailIngestionService
{
    private const int HoldingOrgID = 0;

    private readonly IBlobStorageService _blobStorageService;
    private readonly IEmailParserService _emailParserService;
    private readonly ILogger<DonationEmailIngestionService> _logger;
    private readonly string _connectionString;
    private readonly string? _acsEmailSender;
    private readonly int _createdByUserID;

    public DonationEmailIngestionService(
        IBlobStorageService blobStorageService,
        IEmailParserService emailParserService,
        ILogger<DonationEmailIngestionService> logger)
    {
        _blobStorageService = blobStorageService;
        _emailParserService = emailParserService;
        _logger = logger;

        _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")
            ?? throw new InvalidOperationException("Missing 'SqlConnectionString' environment variable.");

        _acsEmailSender =
            Environment.GetEnvironmentVariable("ACS_EMAIL_SENDER")?
                .Trim();

        _createdByUserID =
            RequiredSecurityConfiguration
                .GetPositiveInt(
                    "AUTOMATED_SERVICE_USER_ID");
    }

    public async Task<DonationIngestionResult> IngestAsync(
        GraphEmailMessage graphMessage,
        byte[] rawMime,
        CancellationToken cancellationToken = default)
    {
        if (graphMessage is null)
        {
            throw new ArgumentNullException(nameof(graphMessage));
        }

        if (rawMime is null || rawMime.Length == 0)
        {
            throw new ArgumentException("Raw MIME email content is required.", nameof(rawMime));
        }

        var emailBlobID = Guid.NewGuid().ToString("N");

        await using (var emailStream = new MemoryStream(rawMime))
        {
            await _blobStorageService.BlobPutAsync(
                blobID: emailBlobID,
                content: emailStream,
                contentType: "message/rfc822",
                cancellationToken: cancellationToken);
        }

        var isInteracEmail = IsInteracDepositEmail(graphMessage);

        ParsedDonationEmail parsed;
        int orgID;

        if (!isInteracEmail)
        {
            parsed = new ParsedDonationEmail
            {
                EmailBlobID = emailBlobID,
                GlobalMessageID = graphMessage.InternetMessageId,
                RecipientEmailAddress = graphMessage.RecipientEmailAddress,
                DateTimeReceived = graphMessage.ReceivedDateTime ?? DateTimeOffset.UtcNow,
                ShortComment = Truncate($"Not an Interac email: {graphMessage.Subject}", 255),

                DonorName = null,
                DonorEmail = null,
                GivingNumber = null,
                Amount = 0,
                ReferenceNumber = null,
                FinancialInstitution = null,
                PaymentKey = null,
                PaymentNotificationID = null
            };

            orgID = HoldingOrgID;
        }
        else
        {
            try
            {
                parsed = _emailParserService.Parse(
                    rawMime,
                    graphMessage,
                    emailBlobID);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to parse Interac email {InternetMessageID}.",
                    graphMessage.InternetMessageId);

                parsed = new ParsedDonationEmail
                {
                    EmailBlobID = emailBlobID,
                    GlobalMessageID = graphMessage.InternetMessageId,
                    RecipientEmailAddress = graphMessage.RecipientEmailAddress,
                    DateTimeReceived = graphMessage.ReceivedDateTime ?? DateTimeOffset.UtcNow,
                    ShortComment = Truncate($"Could not parse Interac email: {graphMessage.Subject}", 255),

                    DonorName = null,
                    DonorEmail = null,
                    GivingNumber = null,
                    Amount = 0,
                    ReferenceNumber = null,
                    FinancialInstitution = null,
                    PaymentKey = null,
                    PaymentNotificationID = null
                };
            }

            orgID = await ResolveOrganizationIDAsync(
                parsed.RecipientEmailAddress,
                cancellationToken);
        }

        var result = await ExecuteIngestStoredProcedureAsync(
            orgID,
            parsed,
            isInteracEmail,
            cancellationToken);

        if (result.WasDuplicate)
        {
            await _blobStorageService.BlobDeleteAsync(
                emailBlobID,
                cancellationToken);
        }

        return result;
    }

    private async Task<int> ResolveOrganizationIDAsync(
        string? recipientEmailAddress,
        CancellationToken cancellationToken)
    {
        var normalizedRecipient = NormalizeEmail(recipientEmailAddress);

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(normalizedRecipient))
        {
            const string sql = @"
                SELECT TOP (1)
                    EMAOrganizationID
                FROM dbo.Organization
                WHERE LOWER(LTRIM(RTRIM(EtransferEmailAddress))) = @RecipientEmailAddress
                   OR LOWER(LTRIM(RTRIM(SandboxEtransferEmail))) = @RecipientEmailAddress
                ORDER BY
                    CASE
                        WHEN LOWER(LTRIM(RTRIM(SandboxEtransferEmail))) = @RecipientEmailAddress
                            THEN 0
                        ELSE 1
                    END,
                    EMAOrganizationID;";

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add("@RecipientEmailAddress", SqlDbType.NVarChar, 512).Value = normalizedRecipient;

            var result = await cmd.ExecuteScalarAsync(cancellationToken);

            if (result is not null && result is not DBNull)
            {
                return Convert.ToInt32(result);
            }
        }

        _logger.LogWarning(
            "Could not resolve organization by recipient email '{RecipientEmailAddress}'. Using holding org {HoldingOrgID}.",
            recipientEmailAddress,
            HoldingOrgID);

        return HoldingOrgID;
    }

    private async Task<DonationIngestionResult> ExecuteIngestStoredProcedureAsync(
        int orgID,
        ParsedDonationEmail parsed,
        bool isInteracEmail,
        CancellationToken cancellationToken)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new SqlCommand("dbo.sp_IngestDonationEmail", conn)
        {
            CommandType = CommandType.StoredProcedure
        };

        cmd.Parameters.Add("@OrgID", SqlDbType.Int).Value = orgID;
        cmd.Parameters.Add("@IsInteracEmail", SqlDbType.Bit).Value = isInteracEmail;

        AddNullableString(cmd, "@DonorName", SqlDbType.VarChar, 255, parsed.DonorName);
        AddNullableString(cmd, "@DonorEmail", SqlDbType.VarChar, 255, parsed.DonorEmail);
        AddNullableString(cmd, "@GivingNumber", SqlDbType.VarChar, 255, parsed.GivingNumber);

        cmd.Parameters.Add("@Amount", SqlDbType.Money).Value = parsed.Amount;

        AddNullableString(cmd, "@ReferenceNumber", SqlDbType.VarChar, 255, parsed.ReferenceNumber);
        AddNullableString(cmd, "@RecipientEmailAddress", SqlDbType.VarChar, 255, parsed.RecipientEmailAddress);
        AddNullableString(cmd, "@GlobalMessageID", SqlDbType.NVarChar, 1024, parsed.GlobalMessageID);
        AddNullableString(cmd, "@EmailBlobID", SqlDbType.VarChar, 36, parsed.EmailBlobID);

        cmd.Parameters.Add("@DateTimeReceived", SqlDbType.DateTime2).Value =
            parsed.DateTimeReceived.UtcDateTime;

        AddNullableString(cmd, "@ShortComment", SqlDbType.VarChar, 255, parsed.ShortComment);
        AddNullableString(cmd, "@FinancialInstitution", SqlDbType.VarChar, 255, parsed.FinancialInstitution);
        AddNullableString(cmd, "@PaymentKey", SqlDbType.VarChar, 255, parsed.PaymentKey);
        AddNullableString(cmd, "@PaymentNotificationID", SqlDbType.VarChar, 255, parsed.PaymentNotificationID);

        cmd.Parameters.Add("@DonationSourceID", SqlDbType.Int).Value = DBNull.Value;
        cmd.Parameters.Add(
            "@CreatedByUserID",
            SqlDbType.Int)
            .Value =
                _createdByUserID;

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("dbo.sp_IngestDonationEmail did not return a result.");
        }

        return new DonationIngestionResult
        {
            NewDonationID = GetInt64(reader, "NewDonationID"),
            EMADonorID = GetInt32(reader, "EMADonorID"),
            WasDonorCreated = GetBoolean(reader, "WasDonorCreated"),
            WasDuplicate = GetBoolean(reader, "WasDuplicate"),

            EMAFundID = GetNullableInt32(reader, "EMAFundID"),
            EMACampusID = GetNullableInt32(reader, "EMACampusID"),
            EMAMethodOfGivingID = GetNullableInt32(reader, "EMAMethodOfGivingID"),

            DonationStepID = HasColumn(reader, "DonationStepID")
                ? GetNullableInt32(reader, "DonationStepID")
                : null,

            WasSuccessful = HasColumn(reader, "WasSuccessful")
                ? GetBoolean(reader, "WasSuccessful")
                : true
        };
    }

    private static long GetInt64(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);

        return reader.IsDBNull(ordinal)
            ? 0
            : reader.GetInt64(ordinal);
    }

    private static int GetInt32(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);

        return reader.IsDBNull(ordinal)
            ? 0
            : reader.GetInt32(ordinal);
    }

    private static int? GetNullableInt32(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);

        return reader.IsDBNull(ordinal)
            ? null
            : reader.GetInt32(ordinal);
    }

    private static bool GetBoolean(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);

        return !reader.IsDBNull(ordinal) && reader.GetBoolean(ordinal);
    }

    private static bool HasColumn(SqlDataReader reader, string columnName)
    {
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (string.Equals(reader.GetName(i), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void AddNullableString(
        SqlCommand cmd,
        string parameterName,
        SqlDbType sqlDbType,
        int size,
        string? value)
    {
        var parameter = cmd.Parameters.Add(parameterName, sqlDbType, size);

        parameter.Value = string.IsNullOrWhiteSpace(value)
            ? DBNull.Value
            : value.Trim();
    }

    private static string? NormalizeEmail(string? email)
    {
        return string.IsNullOrWhiteSpace(email)
            ? null
            : email.Trim().ToLowerInvariant();
    }

    private bool IsInteracDepositEmail(GraphEmailMessage message)
    {
        var subject =
            message.Subject
            ?? string.Empty;

        var isProductionInteracEmail =
            string.Equals(
                message.FromAddress,
                "notify@payments.interac.ca",
                StringComparison.OrdinalIgnoreCase)
            && subject.Contains(
                "Interac e-Transfer",
                StringComparison.OrdinalIgnoreCase)
            && subject.Contains(
                "automatically deposited",
                StringComparison.OrdinalIgnoreCase);

        var isInternalSandboxEmail =
            !string.IsNullOrWhiteSpace(
                _acsEmailSender)
            && string.Equals(
                message.FromAddress,
                _acsEmailSender,
                StringComparison.OrdinalIgnoreCase)
            && subject.Contains(
                "[EMA-SANDBOX]",
                StringComparison.OrdinalIgnoreCase)
            && subject.Contains(
                "Interac e-Transfer",
                StringComparison.OrdinalIgnoreCase);

        return
            isProductionInteracEmail ||
            isInternalSandboxEmail;
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        value = value.Trim();

        return value.Length <= maxLength
            ? value
            : value[..maxLength];
    }
}
